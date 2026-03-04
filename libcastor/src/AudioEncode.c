#include "AudioEncode.h"
#include <libavutil/channel_layout.h>
#include <libavutil/audio_fifo.h>
#include <libswresample/swresample.h>
#include <math.h>

CASTOR_CORE_API int audio_encoder_init(AudioEncoder* enc, int sample_rate, const char* output_path) {
    const AVCodec* codec = avcodec_find_encoder(AV_CODEC_ID_AAC);
    if (!codec) return -1;

    enc->ctx = avcodec_alloc_context3(codec);
    enc->ctx->sample_rate = sample_rate;
    enc->ctx->sample_fmt  = AV_SAMPLE_FMT_FLTP;
    enc->ctx->bit_rate    = 128000;
    av_channel_layout_default(&enc->ctx->ch_layout, 2);

    if (avcodec_open2(enc->ctx, codec, NULL) < 0) return -1;

    /* --- Setup avformat avec le muxer ADTS --- */
    if (avformat_alloc_output_context2(&enc->fmt_ctx, NULL, "adts", output_path) < 0) {
        fprintf(stderr, "[AudioEncoder] avformat_alloc_output_context2 failed\n");
        return -1;
    }

    enc->stream = avformat_new_stream(enc->fmt_ctx, NULL);
    if (!enc->stream) return -1;

    avcodec_parameters_from_context(enc->stream->codecpar, enc->ctx);
    enc->stream->time_base = (AVRational){ 1, sample_rate };

    if (avio_open(&enc->fmt_ctx->pb, output_path, AVIO_FLAG_WRITE) < 0) {
        fprintf(stderr, "[AudioEncoder] avio_open failed: %s\n", output_path);
        return -1;
    }

    if (avformat_write_header(enc->fmt_ctx, NULL) < 0) {
        fprintf(stderr, "[AudioEncoder] avformat_write_header failed\n");
        return -1;
    }

    /* --- Frame / Packet / FIFO --- */
    enc->frame = av_frame_alloc();
    enc->frame->nb_samples  = enc->ctx->frame_size;
    enc->frame->format      = enc->ctx->sample_fmt;
    enc->frame->sample_rate = sample_rate;
    av_channel_layout_copy(&enc->frame->ch_layout, &enc->ctx->ch_layout);
    av_frame_get_buffer(enc->frame, 0);

    enc->pkt          = av_packet_alloc();
    enc->sample_index = 0;
    enc->swr          = NULL;

    enc->fifo = av_audio_fifo_alloc(
        AV_SAMPLE_FMT_FLTP, 2, enc->ctx->frame_size * 4);
    if (!enc->fifo) return -1;

    return 0;
}

/* ------------------------------------------------------------------ *
 *  Interne : envoyer un frame plein à l'encodeur et écrire les paquets
 * ------------------------------------------------------------------ */
static int encode_one_frame(AudioEncoder* enc) {
    av_frame_make_writable(enc->frame);
    enc->frame->nb_samples = enc->ctx->frame_size;

    if (av_audio_fifo_read(enc->fifo,
            (void**)enc->frame->data, enc->ctx->frame_size) < enc->ctx->frame_size)
        return 0;

    enc->frame->pts    = enc->sample_index;
    enc->sample_index += enc->ctx->frame_size;

    if (avcodec_send_frame(enc->ctx, enc->frame) < 0) return -1;

    while (avcodec_receive_packet(enc->ctx, enc->pkt) == 0) {
        enc->pkt->stream_index = enc->stream->index;
        av_packet_rescale_ts(enc->pkt, enc->ctx->time_base, enc->stream->time_base);
        av_interleaved_write_frame(enc->fmt_ctx, enc->pkt);
        av_packet_unref(enc->pkt);
    }
    return 0;
}

/* ------------------------------------------------------------------ *
 *  audio_encoder_encode_frame
 * ------------------------------------------------------------------ */
CASTOR_CORE_API int audio_encoder_encode_frame(AudioEncoder* enc, AVFrame* src) {
    if (!src) return 0;

    /* ---- 1. Créer/recréer le resampler si le format source change ---- */
    int needs_resample =
        src->sample_rate                        != enc->ctx->sample_rate ||
        src->ch_layout.nb_channels              != enc->ctx->ch_layout.nb_channels ||
        (enum AVSampleFormat)src->format        != enc->ctx->sample_fmt;

    if (needs_resample) {
        if (enc->swr) {
            swr_free(&enc->swr);
            enc->swr = NULL;
        }

        int ret = swr_alloc_set_opts2(
            &enc->swr,
            &enc->ctx->ch_layout,
            enc->ctx->sample_fmt,
            enc->ctx->sample_rate,
            &src->ch_layout,
            (enum AVSampleFormat)src->format,
            src->sample_rate,
            0, NULL);

        if (ret < 0 || swr_init(enc->swr) < 0) {
            fprintf(stderr, "[AudioEncoder] swr_alloc/init failed\n");
            return -1;
        }
    }

    /* ---- 2. Rééchantillonner si nécessaire ---- */
    AVFrame* resampled = NULL;

    if (enc->swr) {
        resampled = av_frame_alloc();
        resampled->format      = enc->ctx->sample_fmt;
        resampled->sample_rate = enc->ctx->sample_rate;
        av_channel_layout_copy(&resampled->ch_layout, &enc->ctx->ch_layout);

        resampled->nb_samples = (int)av_rescale_rnd(
            swr_get_delay(enc->swr, src->sample_rate) + src->nb_samples,
            enc->ctx->sample_rate, src->sample_rate, AV_ROUND_UP);

        av_frame_get_buffer(resampled, 0);

        int out_samples = swr_convert(
            enc->swr,
            resampled->data, resampled->nb_samples,
            (const uint8_t**)src->data, src->nb_samples);

        if (out_samples < 0) {
            fprintf(stderr, "[AudioEncoder] swr_convert failed\n");
            av_frame_free(&resampled);
            return -1;
        }
        resampled->nb_samples = out_samples;
    }

    AVFrame* to_write = resampled ? resampled : src;

    /* ---- 3. Écrire dans le FIFO ---- */
    if (av_audio_fifo_write(enc->fifo,
            (void**)to_write->data, to_write->nb_samples) < to_write->nb_samples) {
        fprintf(stderr, "[AudioEncoder] av_audio_fifo_write failed\n");
        if (resampled) av_frame_free(&resampled);
        return -1;
    }

    if (resampled) av_frame_free(&resampled);

    /* ---- 4. Encoder tant qu'on a frame_size samples ---- */
    while (av_audio_fifo_size(enc->fifo) >= enc->ctx->frame_size) {
        int ret = encode_one_frame(enc);
        if (ret < 0) return ret;
    }

    return 0;
}

/* ------------------------------------------------------------------ *
 *  audio_encoder_cleanup — flush le FIFO restant avant de fermer
 * ------------------------------------------------------------------ */
CASTOR_CORE_API void audio_encoder_cleanup(AudioEncoder* enc) {
    /* Flush les samples résiduels dans le FIFO (< frame_size) */
    int leftover = av_audio_fifo_size(enc->fifo);
    if (leftover > 0) {
        av_frame_make_writable(enc->frame);
        enc->frame->nb_samples = leftover;
        av_audio_fifo_read(enc->fifo, (void**)enc->frame->data, leftover);
        enc->frame->pts    = enc->sample_index;
        enc->sample_index += leftover;
        avcodec_send_frame(enc->ctx, enc->frame);
    }

    /* Flush encodeur */
    avcodec_send_frame(enc->ctx, NULL);
    while (avcodec_receive_packet(enc->ctx, enc->pkt) == 0) {
        enc->pkt->stream_index = enc->stream->index;
        av_packet_rescale_ts(enc->pkt, enc->ctx->time_base, enc->stream->time_base);
        av_interleaved_write_frame(enc->fmt_ctx, enc->pkt);
        av_packet_unref(enc->pkt);
    }

    av_write_trailer(enc->fmt_ctx);
    avio_closep(&enc->fmt_ctx->pb);
    avformat_free_context(enc->fmt_ctx);

    av_audio_fifo_free(enc->fifo);
    if (enc->swr) swr_free(&enc->swr);
    avcodec_free_context(&enc->ctx);
    av_frame_free(&enc->frame);
    av_packet_free(&enc->pkt);
}
