#pragma once
#include "castor_api.h"
#include <libavcodec/avcodec.h>
#include <libswscale/swscale.h>

/* Forward declarations */
typedef struct CastorMuxer  CastorMuxer;
typedef struct CastorOutput CastorOutput;

#ifdef __cplusplus
extern "C" {
#endif

/* ================================================================== *
 *  CastorVideoCodec — codec video selectionnable
 * ================================================================== */
typedef enum {
    CASTOR_VCODEC_H264 = 0,  /* libx264 — MP4 / MKV / RTMP */
    CASTOR_VCODEC_VP9  = 1,  /* libvpx-vp9 — WebM          */
} CastorVideoCodec;

/* ================================================================== *
 *  VideoEncoderConfig — parametres d'encodage video.
 *
 *  Deux modes :
 *    - CRF  (cbr=0) : qualite constante, taille variable. Ideal pour fichier.
 *    - CBR  (cbr=1) : debit constant, latence maitrisee.  Ideal pour RTMP.
 *
 *  Helpers fournis :
 *    video_encoder_config_default() -> H264 CRF, preset veryfast, GOP 2s
 *    video_encoder_config_rtmp(bitrate, gop) -> H264 CBR, zerolatency
 * ================================================================== */
typedef struct {
    int              cbr;                /* 0 = CRF (qualite), 1 = CBR (debit constant) */
    int              video_bitrate_kbps; /* debit video en kb/s     — utilise si cbr=1  */
    int              gop_seconds;        /* intervalle keyframe (s) — 0 = defaut (2s)   */
    int              zerolatency;        /* 1 = tune zerolatency    — recommande RTMP   */
    CastorVideoCodec video_codec;        /* codec selectionne                           */
    int              src_width;          /* largeur capture source  — 0 = meme que out  */
    int              src_height;         /* hauteur capture source  — 0 = meme que out  */
    int              crf;                /* valeur CRF explicite    — 0 = defaut codec   */
} VideoEncoderConfig;

static inline VideoEncoderConfig video_encoder_config_default(void)
{
    VideoEncoderConfig c;
    c.cbr                = 0;
    c.video_bitrate_kbps = 0;
    c.gop_seconds        = 2;
    c.zerolatency        = 0;
    c.video_codec        = CASTOR_VCODEC_H264;
    return c;
}

static inline VideoEncoderConfig video_encoder_config_rtmp(int bitrate_kbps, int gop_seconds)
{
    VideoEncoderConfig c;
    c.cbr                = 1;
    c.video_bitrate_kbps = bitrate_kbps > 0 ? bitrate_kbps : 4000;
    c.gop_seconds        = gop_seconds  > 0 ? gop_seconds  : 2;
    c.zerolatency        = 1;
    c.video_codec        = CASTOR_VCODEC_H264;
    return c;
}

/* ================================================================== *
 *  VideoEncoder
 * ================================================================== */
typedef struct {
    AVCodecContext*    ctx;
    AVFrame*           frame;
    AVPacket*          pkt;
    int                frame_index;
    int                fps;             /* fps cible — sauvegarde AVANT avcodec_open2.
                                         * Certains encodeurs (libvpx-vp9) modifient
                                         * ctx->time_base apres open ; on utilise ce
                                         * champ pour calculer frame->pts correctement
                                         * via av_rescale_q, quel que soit le codec. */
    struct SwsContext* sws_ctx;         /* BGRA -> YUV420P */
    int64_t            first_pts;
    int                first_pts_set;
} VideoEncoder;

/*
 * Initialise l'encodeur H.264 avec configuration par defaut (CRF).
 * Pour du streaming, preferer video_encoder_init_ex avec une config RTMP.
 */
CASTOR_CORE_API int  video_encoder_init   (VideoEncoder* enc, int width, int height, int fps);

/*
 * Initialise l'encodeur H.264 avec une configuration complete.
 *
 * enc    : encodeur a initialiser
 * width  : largeur de la source en pixels
 * height : hauteur de la source en pixels
 * fps    : frequence d'images cible
 * cfg    : parametres d'encodage (NULL = defaut CRF)
 *
 * Retourne 0 si succes, -1 en cas d'erreur.
 */
CASTOR_CORE_API int  video_encoder_init_ex(VideoEncoder* enc, int width, int height, int fps,
                                           const VideoEncoderConfig* cfg);

/*
 * Encode un frame BGRA vers H.264 et ecrit les paquets via CastorOutput.
 */
CASTOR_CORE_API int  video_encoder_encode_frame(VideoEncoder* enc, AVFrame* src, CastorOutput* out);

/*
 * Flush les derniers paquets et libere toutes les ressources.
 */
CASTOR_CORE_API void video_encoder_cleanup     (VideoEncoder* enc, CastorOutput* out);

#ifdef __cplusplus
}
#endif
