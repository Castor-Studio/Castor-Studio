#include "Muxer.h"
#include <string.h>
#include <stdio.h>

CASTOR_CORE_API int muxer_open(CastorMuxer* mux, const char* output_path) {
    memset(mux, 0, sizeof(*mux));

    if (avformat_alloc_output_context2(&mux->fmt_ctx, NULL, NULL, output_path) < 0) {
        fprintf(stderr, "[Muxer] avformat_alloc_output_context2 failed pour: %s\n", output_path);
        return -1;
    }

    InitializeCriticalSection(&mux->lock);
    return 0;
}

CASTOR_CORE_API int muxer_add_video_stream(CastorMuxer* mux, AVCodecContext* vctx) {
    mux->video_stream = avformat_new_stream(mux->fmt_ctx, NULL);
    if (!mux->video_stream) {
        fprintf(stderr, "[Muxer] avformat_new_stream (video) failed\n");
        return -1;
    }

    if (avcodec_parameters_from_context(mux->video_stream->codecpar, vctx) < 0) {
        fprintf(stderr, "[Muxer] avcodec_parameters_from_context (video) failed\n");
        return -1;
    }

    mux->video_stream->time_base = vctx->time_base;
    return 0;
}

CASTOR_CORE_API int muxer_add_audio_stream(CastorMuxer* mux, AVCodecContext* actx) {
    mux->audio_stream = avformat_new_stream(mux->fmt_ctx, NULL);
    if (!mux->audio_stream) {
        fprintf(stderr, "[Muxer] avformat_new_stream (audio) failed\n");
        return -1;
    }

    if (avcodec_parameters_from_context(mux->audio_stream->codecpar, actx) < 0) {
        fprintf(stderr, "[Muxer] avcodec_parameters_from_context (audio) failed\n");
        return -1;
    }

    mux->audio_stream->time_base = (AVRational){ 1, actx->sample_rate };
    return 0;
}

CASTOR_CORE_API int muxer_write_header(CastorMuxer* mux) {
    if (!(mux->fmt_ctx->oformat->flags & AVFMT_NOFILE)) {
        if (avio_open(&mux->fmt_ctx->pb, mux->fmt_ctx->url, AVIO_FLAG_WRITE) < 0) {
            fprintf(stderr, "[Muxer] avio_open failed: %s\n", mux->fmt_ctx->url);
            return -1;
        }
    }

    if (avformat_write_header(mux->fmt_ctx, NULL) < 0) {
        fprintf(stderr, "[Muxer] avformat_write_header failed\n");
        return -1;
    }

    return 0;
}

CASTOR_CORE_API int muxer_write_packet(CastorMuxer* mux, AVPacket* pkt) {
    EnterCriticalSection(&mux->lock);
    int ret = av_interleaved_write_frame(mux->fmt_ctx, pkt);
    LeaveCriticalSection(&mux->lock);
    if (ret < 0) {
        char errbuf[64];
        av_strerror(ret, errbuf, sizeof(errbuf));
        fprintf(stderr, "[Muxer] av_interleaved_write_frame: %s\n", errbuf);
    }
    return ret;
}

CASTOR_CORE_API void muxer_close(CastorMuxer* mux) {
    if (!mux->fmt_ctx) return;

    av_write_trailer(mux->fmt_ctx);

    if (!(mux->fmt_ctx->oformat->flags & AVFMT_NOFILE))
        avio_closep(&mux->fmt_ctx->pb);

    avformat_free_context(mux->fmt_ctx);
    mux->fmt_ctx = NULL;

    DeleteCriticalSection(&mux->lock);
}
