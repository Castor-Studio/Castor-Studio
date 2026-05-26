#include "VideoEncode.h"
#include "output/output.h"
#include <libavutil/imgutils.h>
#include <libavutil/opt.h>
#include <libswscale/swscale.h>
#include <stdio.h>

/* ------------------------------------------------------------------ *
 *  video_encoder_init_ex — implementation centrale
 * ------------------------------------------------------------------ */
CASTOR_CORE_API int video_encoder_init_ex(VideoEncoder* enc, int width, int height, int fps,
                                          const VideoEncoderConfig* cfg)
{
    VideoEncoderConfig defaults = video_encoder_config_default();
    if (!cfg) cfg = &defaults;

    const AVCodec* codec = NULL;
    const int use_vp9 = (cfg->video_codec == CASTOR_VCODEC_VP9);

    if (use_vp9) {
        codec = avcodec_find_encoder_by_name("libvpx-vp9");
        if (!codec) {
            fprintf(stderr, "[VideoEncoder] libvpx-vp9 introuvable\n");
            return -1;
        }
    } else {
        /* Preferer libx264 : supporte CBR, preset, tune=zerolatency.
         * h264_mf (Media Foundation) ignore ces options et est deconseille pour RTMP. */
        codec = avcodec_find_encoder_by_name("libx264");
        if (!codec) {
            fprintf(stderr, "[VideoEncoder] libx264 introuvable, fallback vers codec H264 systeme\n");
            codec = avcodec_find_encoder(AV_CODEC_ID_H264);
        }
        if (!codec) {
            fprintf(stderr, "[VideoEncoder] aucun codec H264 disponible\n");
            return -1;
        }
    }
    fprintf(stderr, "[VideoEncoder] codec : %s\n", codec->name);

    enc->ctx = avcodec_alloc_context3(codec);
    if (!enc->ctx) return -1;

    /* gop_seconds == 0 :
     *   - zerolatency (preview) → fps/2 frames = 0,5 s à 30 fps : IDR fréquents,
     *     permet à VLC de rejoindre le flux rapidement après une reconnexion.
     *   - mode normal           → 2 s (défaut standard pour le broadcast). */
    const int gop = cfg->gop_seconds > 0 ? cfg->gop_seconds * fps
                  : (cfg->zerolatency   ? fps / 2 : 2 * fps);

    enc->ctx->width        = width;
    enc->ctx->height       = height;
    enc->ctx->time_base    = (AVRational){ 1, fps };
    enc->ctx->framerate    = (AVRational){ fps, 1 };
    enc->ctx->pix_fmt      = AV_PIX_FMT_YUV420P;
    enc->ctx->gop_size     = gop;
    enc->ctx->max_b_frames = 0;
    enc->ctx->flags       |= AV_CODEC_FLAG_GLOBAL_HEADER;

    enc->first_pts     = 0;
    enc->first_pts_set = 0;

    if (use_vp9) {
        /* --- libvpx-vp9 --- */
        if (cfg->cbr && cfg->video_bitrate_kbps > 0) {
            const int bps = cfg->video_bitrate_kbps * 1000;
            enc->ctx->bit_rate       = bps;
            enc->ctx->rc_min_rate    = bps;
            enc->ctx->rc_max_rate    = bps;
            enc->ctx->rc_buffer_size = cfg->zerolatency ? bps / 2 : bps * 2;
            av_opt_set(enc->ctx->priv_data, "deadline",
                       cfg->zerolatency ? "realtime" : "good", 0);
            av_opt_set(enc->ctx->priv_data, "cpu-used",
                       cfg->zerolatency ? "8" : "4", 0);
        } else {
            /* CQ (constrained quality) — equivalent CRF pour VP9 */
            enc->ctx->bit_rate = 0;
            av_opt_set(enc->ctx->priv_data, "deadline", "good", 0);
            av_opt_set(enc->ctx->priv_data, "cpu-used", "4", 0);
            av_opt_set_int(enc->ctx->priv_data, "crf", 33, AV_OPT_SEARCH_CHILDREN);
        }
    } else {
        /* --- libx264 --- */

        /* Preset */
        av_opt_set(enc->ctx->priv_data, "preset",
                   cfg->zerolatency ? "ultrafast" : "veryfast", 0);

        /* Latence zero (streaming) */
        if (cfg->zerolatency)
            av_opt_set(enc->ctx->priv_data, "tune", "zerolatency", 0);

        /* Mode CBR ou CRF */
        if (cfg->cbr && cfg->video_bitrate_kbps > 0) {
            const int bps = cfg->video_bitrate_kbps * 1000;
            enc->ctx->bit_rate       = bps;
            enc->ctx->rc_min_rate    = bps;
            enc->ctx->rc_max_rate    = bps;
            /* En mode zerolatency (preview live) : VBV = 0.5 s → réduit le buffer
             * encoder côté RTMP sans casser le flux.
             * En mode normal (broadcast) : VBV = 2 s → headroom réseau standard. */
            enc->ctx->rc_buffer_size = cfg->zerolatency ? bps / 2 : bps * 2;
        }
    }

    if (avcodec_open2(enc->ctx, codec, NULL) < 0) {
        fprintf(stderr, "[VideoEncoder] avcodec_open2 failed\n");
        avcodec_free_context(&enc->ctx);
        return -1;
    }

    enc->frame = av_frame_alloc();
    enc->frame->format = AV_PIX_FMT_YUV420P;
    enc->frame->width  = width;
    enc->frame->height = height;
    av_frame_get_buffer(enc->frame, 32);

    enc->sws_ctx = sws_getContext(
        width, height, AV_PIX_FMT_BGRA,
        width, height, AV_PIX_FMT_YUV420P,
        SWS_BILINEAR, NULL, NULL, NULL
    );
    if (!enc->sws_ctx) {
        fprintf(stderr, "[VideoEncoder] sws_getContext failed\n");
        avcodec_free_context(&enc->ctx);
        av_frame_free(&enc->frame);
        return -1;
    }

    enc->pkt         = av_packet_alloc();
    enc->frame_index = 0;
    return 0;
}

/* Compatibilite — utilise la config par defaut (CRF) */
CASTOR_CORE_API int video_encoder_init(VideoEncoder* enc, int width, int height, int fps)
{
    return video_encoder_init_ex(enc, width, height, fps, NULL);
}

/* ------------------------------------------------------------------ *
 *  Flush interne
 * ------------------------------------------------------------------ */
static int flush_encoder(VideoEncoder* enc, CastorOutput* out)
{
    if (avcodec_send_frame(enc->ctx, NULL) < 0) return -1;
    while (avcodec_receive_packet(enc->ctx, enc->pkt) == 0) {
        enc->pkt->stream_index = out->video_stream_index;
        av_packet_rescale_ts(enc->pkt, enc->ctx->time_base,
                             out->video_stream_time_base);
        output_write_packet(out, enc->pkt);
        av_packet_unref(enc->pkt);
    }
    return 0;
}

/* ------------------------------------------------------------------ *
 *  video_encoder_cleanup
 * ------------------------------------------------------------------ */
CASTOR_CORE_API void video_encoder_cleanup(VideoEncoder* enc, CastorOutput* out)
{
    flush_encoder(enc, out);
    sws_freeContext(enc->sws_ctx);
    avcodec_free_context(&enc->ctx);
    av_frame_free(&enc->frame);
    av_packet_free(&enc->pkt);
}

/* ------------------------------------------------------------------ *
 *  video_encoder_encode_frame
 * ------------------------------------------------------------------ */
CASTOR_CORE_API int video_encoder_encode_frame(VideoEncoder* enc, AVFrame* src, CastorOutput* out)
{
    av_frame_make_writable(enc->frame);

    sws_scale(
        enc->sws_ctx,
        (const uint8_t* const*)src->data, src->linesize,
        0, src->height,
        enc->frame->data, enc->frame->linesize
    );

    if (!enc->first_pts_set) {
        enc->first_pts     = src->pts;
        enc->first_pts_set = 1;
    }

    enc->frame->pts = enc->frame_index++;

    int ret = avcodec_send_frame(enc->ctx, enc->frame);
    if (ret < 0) {
        char errbuf[64];
        av_strerror(ret, errbuf, sizeof(errbuf));
        fprintf(stderr, "[VideoEncoder] send_frame: %s\n", errbuf);
        return ret;
    }

    while (avcodec_receive_packet(enc->ctx, enc->pkt) == 0) {
        enc->pkt->stream_index = out->video_stream_index;
        av_packet_rescale_ts(enc->pkt, enc->ctx->time_base,
                             out->video_stream_time_base);
        output_write_packet(out, enc->pkt);
        av_packet_unref(enc->pkt);
    }
    return 0;
}
