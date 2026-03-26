#pragma once

#include "castor_api.h"
#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ================================================================== *
 *  CastorOutput — interface generique pour toute sortie encodee.
 *
 *  Chaque implementation (fichier, RTMP, ...) expose exactement
 *  cette interface via un vtable embarque en premiere position du
 *  struct concret, permettant un cast C simple (pas de C++).
 *
 *  Cycle de vie attendu :
 *    1. xxx_output_create(...)         -> CastorOutput*
 *    2. output_add_video_stream(o, v)
 *    3. output_add_audio_stream(o, a)
 *    4. output_write_header(o)
 *    5. output_write_packet(o, pkt)    [en boucle, thread-safe]
 *    6. output_close(o)
 *    7. output_destroy(&o)             -> libere la memoire, met le ptr a NULL
 *
 *  Extension future : pour record + stream simultanement,
 *  stocker un tableau CastorOutput* outputs[CASTOR_MAX_OUTPUTS]
 *  dans StreamState et appeler chaque output dans muxer_write_packet.
 * ================================================================== */

typedef struct CastorOutput CastorOutput;

struct CastorOutput {
    /* Ajoute le stream video au container (apres open, avant write_header).
     * Remplit video_stream_index et video_stream_time_base apres succes. */
    int  (*add_video_stream)(CastorOutput* self, AVCodecContext* vctx);

    /* Ajoute le stream audio au container (apres open, avant write_header).
     * Remplit audio_stream_index et audio_stream_time_base apres succes. */
    int  (*add_audio_stream)(CastorOutput* self, AVCodecContext* actx);

    /* Ecrit l'en-tete du container et ouvre la connexion / le fichier. */
    int  (*write_header)(CastorOutput* self);

    /* Ecrit un paquet encode. Thread-safe (protege par CRITICAL_SECTION). */
    int  (*write_packet)(CastorOutput* self, AVPacket* pkt);

    /* Flush, ecrit le trailer et ferme la connexion / le fichier. */
    void (*close)(CastorOutput* self);

    /* Libere toutes les ressources allouees par xxx_output_create. */
    void (*destroy)(CastorOutput* self);

    /* Remplis par add_video_stream / add_audio_stream.
     * Permettent aux encodeurs de setter stream_index et rescale_ts
     * sans connaitre l'implementation interne du muxer. */
    int        video_stream_index;
    AVRational video_stream_time_base;
    int        audio_stream_index;
    AVRational audio_stream_time_base;
};

/* ------------------------------------------------------------------ *
 *  Helpers inline — evitent d'appeler le vtable manuellement
 * ------------------------------------------------------------------ */

static inline int output_add_video_stream(CastorOutput* o, AVCodecContext* vctx)
{
    return o->add_video_stream(o, vctx);
}

static inline int output_add_audio_stream(CastorOutput* o, AVCodecContext* actx)
{
    return o->add_audio_stream(o, actx);
}

static inline int output_write_header(CastorOutput* o)
{
    return o->write_header(o);
}

static inline int output_write_packet(CastorOutput* o, AVPacket* pkt)
{
    return o->write_packet(o, pkt);
}

static inline void output_close(CastorOutput* o)
{
    o->close(o);
}

static inline void output_destroy(CastorOutput** o)
{
    if (o && *o) {
        (*o)->destroy(*o);
        *o = NULL;
    }
}

#ifdef __cplusplus
}
#endif
