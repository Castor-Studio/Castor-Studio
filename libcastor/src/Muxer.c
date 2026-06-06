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

    /* vctx->time_base peut avoir ete modifie par certains encodeurs
     * (libvpx-vp9) apres avcodec_open2. On derive la stream time_base depuis
     * vctx->framerate = {fps, 1} qui, lui, n'est pas touche par libvpx.
     * av_inv_q({fps, 1}) = {1, fps} — unite "un tick par frame". */
    mux->video_stream->time_base = (vctx->framerate.num > 0 && vctx->framerate.den > 0)
        ? av_inv_q(vctx->framerate)
        : vctx->time_base;
    mux->video_stream->avg_frame_rate = vctx->framerate;
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

    fprintf(stderr, "[Muxer] write_header OK — connexion etablie : %s\n", mux->fmt_ctx->url);
    return 0;
}

CASTOR_CORE_API int muxer_write_packet(CastorMuxer* mux, AVPacket* pkt) {
    /* Si une erreur fatale s'est deja produite, on ne retente pas.
     * Cela evite le spam de logs (ex: connexion RTMP perdue = des centaines
     * de WSAECONNABORTED en boucle jusqu'a l'arret manuel). */
    if (mux->fatal_error) return mux->fatal_error;

    EnterCriticalSection(&mux->lock);
    int ret = av_interleaved_write_frame(mux->fmt_ctx, pkt);
    LeaveCriticalSection(&mux->lock);

    if (ret < 0) {
        char errbuf[64];
        av_strerror(ret, errbuf, sizeof(errbuf));
        fprintf(stderr, "[Muxer] av_interleaved_write_frame: %s — connexion perdue, arret de l'ecriture\n", errbuf);
        mux->fatal_error = ret;
    }
    return ret;
}

CASTOR_CORE_API void muxer_close(CastorMuxer* mux) {
    if (!mux->fmt_ctx) return;

    /* N'appelle av_write_trailer que si la connexion a été ouverte (pb != NULL).
     * Si avio_open a échoué, pb est NULL et av_write_trailer crasherait (SEH). */
    if (mux->fmt_ctx->pb || (mux->fmt_ctx->oformat->flags & AVFMT_NOFILE))
        av_write_trailer(mux->fmt_ctx);

    if (!(mux->fmt_ctx->oformat->flags & AVFMT_NOFILE))
        avio_closep(&mux->fmt_ctx->pb);

    avformat_free_context(mux->fmt_ctx);
    mux->fmt_ctx = NULL;

    DeleteCriticalSection(&mux->lock);
}
