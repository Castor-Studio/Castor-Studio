#pragma once
#include "castor_api.h"
#include <libavcodec/avcodec.h>
#include <libswscale/swscale.h>

/* Forward declaration — evite d'inclure Muxer.h et windows.h ici */
typedef struct CastorMuxer CastorMuxer;

#ifdef __cplusplus
extern "C" {
#endif

typedef struct {
    AVCodecContext* ctx;
    AVFrame*        frame;
    AVPacket*       pkt;

    int             frame_index;
    struct SwsContext* sws_ctx;   /* BGRA -> YUV420P */

    int64_t         first_pts;
    int             first_pts_set;
} VideoEncoder;

/*
 * Initialise l'encodeur H.264 (libx264).
 * Configure le codec, alloue le frame YUV420P de travail et le SwsContext
 * pour la conversion BGRA->YUV420P.
 *
 * enc    : encodeur a initialiser
 * width  : largeur de la source en pixels
 * height : hauteur de la source en pixels
 * fps    : frequence d'images cible
 *
 * Retourne 0 si succes, -1 en cas d'erreur.
 */
CASTOR_CORE_API int  video_encoder_init(VideoEncoder* enc, int width, int height, int fps);

/*
 * Convertit un frame BGRA en YUV420P puis l'encode en H.264.
 * Ecrit les paquets dans le container MP4 via le muxer partage.
 *
 * enc : encodeur initialise
 * src : frame video source au format BGRA (issu de la capture)
 * mux : muxer MP4 partage (thread-safe)
 *
 * Retourne 0 si succes, code d'erreur FFmpeg negatif sinon.
 */
CASTOR_CORE_API int  video_encoder_encode_frame(VideoEncoder* enc, AVFrame* src, CastorMuxer* mux);

/*
 * Flush les derniers paquets en attente dans l'encodeur,
 * puis libere le codec context, le frame et le SwsContext.
 *
 * enc : encodeur a nettoyer
 * mux : muxer MP4 partage (pour le flush final)
 */
CASTOR_CORE_API void video_encoder_cleanup(VideoEncoder* enc, CastorMuxer* mux);

#ifdef __cplusplus
}
#endif
