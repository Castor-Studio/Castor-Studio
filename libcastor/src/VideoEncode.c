#include "VideoEncode.h"
#include "Muxer.h"
#include <libavutil/imgutils.h>
#include <libavutil/opt.h>
#include <libswscale/swscale.h>

CASTOR_CORE_API int video_encoder_init(VideoEncoder* enc, int width, int height, int fps) {
    const AVCodec* codec = avcodec_find_encoder(AV_CODEC_ID_H264);
    if (!codec) return -1;

    enc->ctx = avcodec_alloc_context3(codec);
    enc->ctx->width       = width;
    enc->ctx->height      = height;
    enc->ctx->time_base   = (AVRational){ 1, fps };
    enc->ctx->framerate   = (AVRational){ fps, 1 };
    enc->ctx->pix_fmt     = AV_PIX_FMT_YUV420P;
    enc->ctx->gop_size    = 12;
    enc->ctx->max_b_frames = 0;
    enc->first_pts     = 0;
    enc->first_pts_set = 0;
    
    av_opt_set(enc->ctx->priv_data, "preset", "veryfast", 0);

    if (avcodec_open2(enc->ctx, codec, NULL) < 0) return -1;

    // fprintf(stderr, "[VideoEncoder] time_base effectif = %d/%d\n",
    //         enc->ctx->time_base.num, enc->ctx->time_base.den);

    enc->frame = av_frame_alloc();
    enc->frame->format = AV_PIX_FMT_YUV420P;
    enc->frame->width  = width;
    enc->frame->height = height;
    av_frame_get_buffer(enc->frame, 32);

    // ← Creer le contexte sws une seule fois ici
    enc->sws_ctx = sws_getContext(
        width, height, AV_PIX_FMT_BGRA,      // src = WGC
        width, height, AV_PIX_FMT_YUV420P,   // dst = encodeur
        SWS_BILINEAR, NULL, NULL, NULL
    );
    if (!enc->sws_ctx) return -1;

    enc->pkt         = av_packet_alloc();
    enc->frame_index = 0;
    return 0;
}

static int flush_encoder(VideoEncoder* enc, CastorMuxer* mux) {
    if (avcodec_send_frame(enc->ctx, NULL) < 0) return -1;
    while (avcodec_receive_packet(enc->ctx, enc->pkt) == 0) {
        enc->pkt->stream_index = mux->video_stream->index;
        av_packet_rescale_ts(enc->pkt, enc->ctx->time_base, mux->video_stream->time_base);
        muxer_write_packet(mux, enc->pkt);
        av_packet_unref(enc->pkt);
    }
    return 0;
}

CASTOR_CORE_API void video_encoder_cleanup(VideoEncoder* enc, CastorMuxer* mux) {
    flush_encoder(enc, mux);
    sws_freeContext(enc->sws_ctx);
    avcodec_free_context(&enc->ctx);
    av_frame_free(&enc->frame);
    av_packet_free(&enc->pkt);
}

CASTOR_CORE_API int video_encoder_encode_frame(VideoEncoder* enc, AVFrame* src, CastorMuxer* mux) {
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
        enc->pkt->stream_index = mux->video_stream->index;
        av_packet_rescale_ts(enc->pkt, enc->ctx->time_base, mux->video_stream->time_base);
        muxer_write_packet(mux, enc->pkt);
        av_packet_unref(enc->pkt);
    }
    return 0;
}