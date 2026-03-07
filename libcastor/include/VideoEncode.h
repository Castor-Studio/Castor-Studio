#pragma once
#include "castor_api.h"
#include <stdio.h>
#include <libavcodec/avcodec.h>
#include <libswscale/swscale.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct {
    AVCodecContext* ctx;
    AVFrame* frame;
    AVPacket* pkt;

    int frame_index;
    struct SwsContext* sws_ctx;      // BGRA→YUV420P

    int64_t     first_pts;
    int         first_pts_set;
} VideoEncoder;

/*
 * Initialise l'encodeur H.264 (libx264).
 * Configure le codec, alloue le frame YUV420P de travail et le SwsContext
 * pour la conversion BGRA→YUV420P.
 *
 * enc    : encodeur à initialiser
 * width  : largeur de la source en pixels
 * height : hauteur de la source en pixels
 * fps    : fréquence d'images cible
 *
 * Retourne 0 si succès, -1 en cas d'erreur.
 */
CASTOR_CORE_API int  video_encoder_init(VideoEncoder* enc, int width, int height, int fps);

/*
 * Convertit un frame BGRA en YUV420P puis l'encode en H.264.
 * Ecrit les NAL units directement dans le fichier de sortie.
 *
 * enc : encodeur initialisé
 * src : frame vidéo source au format BGRA (issu de la capture)
 * out : fichier de sortie ouvert en écriture binaire (.h264)
 *
 * Retourne 0 si succès, code d'erreur FFmpeg négatif sinon.
 */
CASTOR_CORE_API int  video_encoder_encode_frame(VideoEncoder* enc, AVFrame* src, FILE* out);

/*
 * Flush les derniers paquets en attente dans l'encodeur,
 * puis libère le codec context, le frame et le SwsContext.
 *
 * enc : encodeur à nettoyer
 * out : fichier de sortie (pour le flush final)
 */
CASTOR_CORE_API void video_encoder_cleanup(VideoEncoder* enc, FILE* out);

#ifdef __cplusplus
}
#endif
