#include "output/rtmp_output.h"
#include "Muxer.h"

#include <stdlib.h>
#include <stdio.h>
#include <string.h>

#define RTMP_DEFAULT_VIDEO_BITRATE_KBPS  4000
#define RTMP_DEFAULT_AUDIO_BITRATE_KBPS   128
#define RTMP_DEFAULT_GOP_SECONDS            2

/* ------------------------------------------------------------------ *
 *  Structure concrete
 * ------------------------------------------------------------------ */
typedef struct {
    CastorOutput     base;    /* vtable — doit etre en premiere position */
    CastorMuxer      mux;
    RtmpOutputConfig config;
} CastorRtmpOutput;

/* ------------------------------------------------------------------ *
 *  Implementation du vtable
 * ------------------------------------------------------------------ */

static int rtmp_add_video_stream(CastorOutput* self, AVCodecContext* vctx)
{
    CastorRtmpOutput* ro = (CastorRtmpOutput*)self;
    if (muxer_add_video_stream(&ro->mux, vctx) < 0) return -1;
    self->video_stream_index     = ro->mux.video_stream->index;
    self->video_stream_time_base = ro->mux.video_stream->time_base;
    return 0;
}

static int rtmp_add_audio_stream(CastorOutput* self, AVCodecContext* actx)
{
    CastorRtmpOutput* ro = (CastorRtmpOutput*)self;
    if (muxer_add_audio_stream(&ro->mux, actx) < 0) return -1;
    self->audio_stream_index     = ro->mux.audio_stream->index;
    self->audio_stream_time_base = ro->mux.audio_stream->time_base;
    return 0;
}

static int rtmp_write_header(CastorOutput* self)
{
    CastorRtmpOutput* ro = (CastorRtmpOutput*)self;
    if (muxer_write_header(&ro->mux) < 0) return -1;

    /* avformat_write_header peut modifier les time_base des streams
     * (FLV passe typiquement a {1,1000} = millisecondes).
     * On relit les valeurs APRES l'ecriture du header. */
    self->video_stream_time_base = ro->mux.video_stream->time_base;
    self->audio_stream_time_base = ro->mux.audio_stream->time_base;

    fprintf(stderr, "[RtmpOutput] time_base video effectif : %d/%d\n",
            self->video_stream_time_base.num, self->video_stream_time_base.den);
    fprintf(stderr, "[RtmpOutput] time_base audio effectif : %d/%d\n",
            self->audio_stream_time_base.num, self->audio_stream_time_base.den);
    return 0;
}

static int rtmp_write_packet(CastorOutput* self, AVPacket* pkt)
{
    CastorRtmpOutput* ro = (CastorRtmpOutput*)self;
    return muxer_write_packet(&ro->mux, pkt);
}

static void rtmp_close(CastorOutput* self)
{
    CastorRtmpOutput* ro = (CastorRtmpOutput*)self;
    muxer_close(&ro->mux);
}

static void rtmp_destroy(CastorOutput* self)
{
    free(self);
}

static const CastorOutput k_rtmp_vtable = {
    .add_video_stream = rtmp_add_video_stream,
    .add_audio_stream = rtmp_add_audio_stream,
    .write_header     = rtmp_write_header,
    .write_packet     = rtmp_write_packet,
    .close            = rtmp_close,
    .destroy          = rtmp_destroy,
};

/* ------------------------------------------------------------------ *
 *  Ouverture du contexte FLV/RTMP
 *
 *  avformat_alloc_output_context2 avec le format "flv" force le
 *  container FLV requis par le protocole RTMP, meme si FFmpeg ne
 *  detecte pas automatiquement le format depuis l'URL.
 * ------------------------------------------------------------------ */
static int rtmp_muxer_open(CastorMuxer* mux, const char* url)
{
    memset(mux, 0, sizeof(*mux));

    if (avformat_alloc_output_context2(&mux->fmt_ctx, NULL, "flv", url) < 0) {
        fprintf(stderr, "[RtmpOutput] avformat_alloc_output_context2 failed pour: %s\n", url);
        return -1;
    }

    InitializeCriticalSection(&mux->lock);
    return 0;
}

/* ------------------------------------------------------------------ *
 *  Factory
 * ------------------------------------------------------------------ */

CASTOR_CORE_API CastorOutput* rtmp_output_create(const RtmpOutputConfig* config)
{
    if (!config || config->url[0] == '\0') {
        fprintf(stderr, "[RtmpOutput] configuration invalide (url vide)\n");
        return NULL;
    }

    CastorRtmpOutput* ro = (CastorRtmpOutput*)calloc(1, sizeof(CastorRtmpOutput));
    if (!ro) return NULL;

    ro->base   = k_rtmp_vtable;
    ro->config = *config;

    /* Appliquer les defauts si les valeurs sont a 0 */
    if (ro->config.video_bitrate_kbps <= 0)
        ro->config.video_bitrate_kbps = RTMP_DEFAULT_VIDEO_BITRATE_KBPS;
    if (ro->config.audio_bitrate_kbps <= 0)
        ro->config.audio_bitrate_kbps = RTMP_DEFAULT_AUDIO_BITRATE_KBPS;
    if (ro->config.gop_seconds <= 0)
        ro->config.gop_seconds = RTMP_DEFAULT_GOP_SECONDS;

    if (rtmp_muxer_open(&ro->mux, ro->config.url) < 0) {
        free(ro);
        return NULL;
    }

    fprintf(stderr, "[RtmpOutput] cree : %s (%d kbps video / %d kbps audio / GOP %ds)\n",
            ro->config.url,
            ro->config.video_bitrate_kbps,
            ro->config.audio_bitrate_kbps,
            ro->config.gop_seconds);

    return (CastorOutput*)ro;
}
