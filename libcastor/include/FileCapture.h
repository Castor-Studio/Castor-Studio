#pragma once
#include "castor_api.h"
#include <libavutil/frame.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ------------------------------------------------------------------ *
 *  FileCapture — démuxeur partagé pour les sources fichier A/V
 *
 *  Un seul AVFormatContext lit les paquets audio et vidéo du fichier.
 *  Un thread interne décode, convertit et distribue dans deux queues
 *  (video_queue / audio_queue), en cadençant la sortie sur le PTS
 *  natif du fichier.  VideoCapture et AudioCapture peuvent ainsi
 *  utiliser les mêmes frames sans dérive d'horloge indépendante.
 * ------------------------------------------------------------------ */
typedef struct FileCaptureContext FileCaptureContext;

/* Ouvre le fichier et démarre le thread demux.
 * Retourne NULL en cas d'erreur. */
CASTOR_CORE_API FileCaptureContext* file_capture_create(const char* path, int loop);

/* Stoppe le thread demux et libère toutes les ressources.
 * *ctx est mis à NULL après l'appel. */
CASTOR_CORE_API void file_capture_destroy(FileCaptureContext** ctx);

/* Bloquant — attend le prochain frame vidéo BGRA dans la queue.
 * Retourne NULL si le FileCapture est arrêté ou si le fichier est épuisé. */
CASTOR_CORE_API AVFrame* file_capture_next_video_frame(FileCaptureContext* ctx);

/* Bloquant — attend le prochain frame audio FLTP 48kHz stéréo dans la queue.
 * Retourne NULL si le FileCapture est arrêté ou si le fichier est épuisé. */
CASTOR_CORE_API AVFrame* file_capture_next_audio_frame(FileCaptureContext* ctx);

CASTOR_CORE_API int file_capture_width(FileCaptureContext* ctx);
CASTOR_CORE_API int file_capture_height(FileCaptureContext* ctx);
CASTOR_CORE_API int file_capture_sample_rate(FileCaptureContext* ctx);
CASTOR_CORE_API int file_capture_channels(FileCaptureContext* ctx);
CASTOR_CORE_API int file_capture_has_video(FileCaptureContext* ctx);
CASTOR_CORE_API int file_capture_has_audio(FileCaptureContext* ctx);

#ifdef __cplusplus
}
#endif
