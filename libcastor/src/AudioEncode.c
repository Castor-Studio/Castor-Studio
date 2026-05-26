#include "AudioEncode.h"
#include "output/output.h"
#include <libavutil/channel_layout.h>
#include <libavutil/audio_fifo.h>
#include <libswresample/swresample.h>
#include <stdio.h>

/* ------------------------------------------------------------------ *
 *  audio_encoder_init_ex — implementation centrale
 * ------------------------------------------------------------------ */
CASTOR_CORE_API int audio_encoder_init_ex(AudioEncoder* enc, int sample_rate,
                                          const AudioEncoderConfig* cfg)
{
    AudioEncoderConfig defaults = audio_encoder_config_default();
    if (!cfg) cfg = &defaults;

    const AVCodec* codec = NULL;
    if (cfg->audio_codec == CASTOR_ACODEC_OPUS) {
        codec = avcodec_find_encoder_by_name("libopus");
        if (!codec) {
            fprintf(stderr, "[AudioEncoder] libopus introuvable\n");
            return -1;
        }
    } else {
        codec = avcodec_find_encoder(AV_CODEC_ID_AAC);
        if (!codec) {
            fprintf(stderr, "[AudioEncoder] codec AAC introuvable\n");
            return -1;
        }
    }
    fprintf(stderr, "[AudioEncoder] codec : %s\n", codec->name);

    enc->ctx = avcodec_alloc_context3(codec);
    if (!enc->ctx) return -1;

    enc->ctx->sample_rate = sample_rate;
    enc->ctx->bit_rate    = (cfg->audio_bitrate_kbps > 0
                             ? cfg->audio_bitrate_kbps
                             : 128) * 1000;
    av_channel_layout_default(&enc->ctx->ch_layout, 2);

    /* Choisir le meilleur sample_fmt supporte par ce codec.
     * On prefere FLTP (planar float) ; certains codecs (ex: libopus) peuvent
     * preferer FLT (interleave) ou S16. */
    enum AVSampleFormat preferred = AV_SAMPLE_FMT_FLTP;
    if (codec->sample_fmts) {
        int fltp_ok = 0;
        for (const enum AVSampleFormat* f = codec->sample_fmts;
             *f != AV_SAMPLE_FMT_NONE; f++) {
            if (*f == preferred) { fltp_ok = 1; break; }
        }
        if (!fltp_ok)
            preferred = codec->sample_fmts[0];
    }
    enc->ctx->sample_fmt = preferred;

    if (avcodec_open2(enc->ctx, codec, NULL) < 0) {
        fprintf(stderr, "[AudioEncoder] avcodec_open2 failed\n");
        avcodec_free_context(&enc->ctx);
        return -1;
    }

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
        enc->ctx->sample_fmt, 2, enc->ctx->frame_size * 4);
    if (!enc->fifo) {
        avcodec_free_context(&enc->ctx);
        av_frame_free(&enc->frame);
        av_packet_free(&enc->pkt);
        return -1;
    }

    return 0;
}

/* Compatibilite — utilise la config par defaut */
CASTOR_CORE_API int audio_encoder_init(AudioEncoder* enc, int sample_rate)
{
    return audio_encoder_init_ex(enc, sample_rate, NULL);
}

/* ------------------------------------------------------------------ *
 *  Interne : encoder un frame plein depuis le FIFO
 * ------------------------------------------------------------------ */
static int encode_one_frame(AudioEncoder* enc, CastorOutput* out)
{
    av_frame_make_writable(enc->frame);
    enc->frame->nb_samples = enc->ctx->frame_size;

    if (av_audio_fifo_read(enc->fifo,
            (void**)enc->frame->data, enc->ctx->frame_size) < enc->ctx->frame_size)
        return 0;

    enc->frame->pts    = enc->sample_index;
    enc->sample_index += enc->ctx->frame_size;

    if (avcodec_send_frame(enc->ctx, enc->frame) < 0) return -1;

    while (avcodec_receive_packet(enc->ctx, enc->pkt) == 0) {
        enc->pkt->stream_index = out->audio_stream_index;
        av_packet_rescale_ts(enc->pkt, enc->ctx->time_base,
                             out->audio_stream_time_base);
        output_write_packet(out, enc->pkt);
        av_packet_unref(enc->pkt);
    }
    return 0;
}

/* ------------------------------------------------------------------ *
 *  audio_encoder_encode_frame
 * ------------------------------------------------------------------ */
CASTOR_CORE_API int audio_encoder_encode_frame(AudioEncoder* enc, AVFrame* src, CastorOutput* out)
{
    if (!src) return 0;

    /* 1. Creer/recreer le resampler si le format source change */
    int needs_resample =
        src->sample_rate               != enc->ctx->sample_rate    ||
        src->ch_layout.nb_channels     != enc->ctx->ch_layout.nb_channels ||
        (enum AVSampleFormat)src->format != enc->ctx->sample_fmt;

    if (needs_resample) {
        if (enc->swr) { swr_free(&enc->swr); enc->swr = NULL; }

        int ret = swr_alloc_set_opts2(
            &enc->swr,
            &enc->ctx->ch_layout, enc->ctx->sample_fmt, enc->ctx->sample_rate,
            &src->ch_layout, (enum AVSampleFormat)src->format, src->sample_rate,
            0, NULL);

        if (ret < 0 || swr_init(enc->swr) < 0) {
            fprintf(stderr, "[AudioEncoder] swr_alloc/init failed\n");
            return -1;
        }
    }

    /* 2. Reechantillonner si necessaire */
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

    /* 3. Ecrire dans le FIFO */
    if (av_audio_fifo_write(enc->fifo,
            (void**)to_write->data, to_write->nb_samples) < to_write->nb_samples) {
        fprintf(stderr, "[AudioEncoder] av_audio_fifo_write failed\n");
        if (resampled) av_frame_free(&resampled);
        return -1;
    }
    if (resampled) av_frame_free(&resampled);

    /* 4. Encoder tant qu'on a frame_size samples */
    while (av_audio_fifo_size(enc->fifo) >= enc->ctx->frame_size) {
        int ret = encode_one_frame(enc, out);
        if (ret < 0) return ret;
    }

    return 0;
}

/* ------------------------------------------------------------------ *
 *  audio_encoder_cleanup
 * ------------------------------------------------------------------ */
CASTOR_CORE_API void audio_encoder_cleanup(AudioEncoder* enc, CastorOutput* out)
{
    /* Flush les samples residuels dans le FIFO (< frame_size) */
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
        enc->pkt->stream_index = out->audio_stream_index;
        av_packet_rescale_ts(enc->pkt, enc->ctx->time_base,
                             out->audio_stream_time_base);
        output_write_packet(out, enc->pkt);
        av_packet_unref(enc->pkt);
    }

    av_audio_fifo_free(enc->fifo);
    if (enc->swr) swr_free(&enc->swr);
    avcodec_free_context(&enc->ctx);
    av_frame_free(&enc->frame);
    av_packet_free(&enc->pkt);
}
