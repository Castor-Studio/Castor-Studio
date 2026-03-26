#include "output/file_output.h"
#include "Muxer.h"

#include <stdlib.h>
#include <stdio.h>
#include <string.h>

/* ------------------------------------------------------------------ *
 *  Structure concrete (invisible hors de ce fichier)
 * ------------------------------------------------------------------ */
typedef struct {
    CastorOutput base;   /* vtable — doit etre en premiere position */
    CastorMuxer  mux;
} CastorFileOutput;

/* ------------------------------------------------------------------ *
 *  Implementation du vtable
 * ------------------------------------------------------------------ */

static int file_add_video_stream(CastorOutput* self, AVCodecContext* vctx)
{
    CastorFileOutput* fo = (CastorFileOutput*)self;
    if (muxer_add_video_stream(&fo->mux, vctx) < 0) return -1;
    self->video_stream_index     = fo->mux.video_stream->index;
    self->video_stream_time_base = fo->mux.video_stream->time_base;
    return 0;
}

static int file_add_audio_stream(CastorOutput* self, AVCodecContext* actx)
{
    CastorFileOutput* fo = (CastorFileOutput*)self;
    if (muxer_add_audio_stream(&fo->mux, actx) < 0) return -1;
    self->audio_stream_index     = fo->mux.audio_stream->index;
    self->audio_stream_time_base = fo->mux.audio_stream->time_base;
    return 0;
}

static int file_write_header(CastorOutput* self)
{
    CastorFileOutput* fo = (CastorFileOutput*)self;
    if (muxer_write_header(&fo->mux) < 0) return -1;

    /* Relire les time_base apres write_header (le muxer peut les modifier). */
    self->video_stream_time_base = fo->mux.video_stream->time_base;
    self->audio_stream_time_base = fo->mux.audio_stream->time_base;
    return 0;
}

static int file_write_packet(CastorOutput* self, AVPacket* pkt)
{
    CastorFileOutput* fo = (CastorFileOutput*)self;
    return muxer_write_packet(&fo->mux, pkt);
}

static void file_close(CastorOutput* self)
{
    CastorFileOutput* fo = (CastorFileOutput*)self;
    muxer_close(&fo->mux);
}

static void file_destroy(CastorOutput* self)
{
    free(self);
}

static const CastorOutput k_file_vtable = {
    .add_video_stream = file_add_video_stream,
    .add_audio_stream = file_add_audio_stream,
    .write_header     = file_write_header,
    .write_packet     = file_write_packet,
    .close            = file_close,
    .destroy          = file_destroy,
};

/* ------------------------------------------------------------------ *
 *  Factory
 * ------------------------------------------------------------------ */

CASTOR_CORE_API CastorOutput* file_output_create(const char* path)
{
    if (!path || path[0] == '\0') {
        fprintf(stderr, "[FileOutput] chemin invalide\n");
        return NULL;
    }

    CastorFileOutput* fo = (CastorFileOutput*)calloc(1, sizeof(CastorFileOutput));
    if (!fo) return NULL;

    fo->base = k_file_vtable;

    if (muxer_open(&fo->mux, path) < 0) {
        free(fo);
        return NULL;
    }

    return (CastorOutput*)fo;
}
