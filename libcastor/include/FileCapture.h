#pragma once
#include "castor_api.h"
#include <libavutil/frame.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct FileCaptureContext FileCaptureContext;

/* Ouvre le fichier et démarre le thread demux.
 * Retourne NULL en cas d'erreur. */
CASTOR_CORE_API FileCaptureContext* file_capture_create(const char* path, int loop);

/* Signale l'arrêt immédiat : ferme les queues pour débloquer tout appel
 * bloquant à file_capture_next_*_frame.
 * À appeler AVANT d'attendre les threads consommateurs, puis appeler
 * file_capture_destroy pour libérer les ressources. */
CASTOR_CORE_API void file_capture_signal_stop(FileCaptureContext* ctx);

/* Stoppe le thread demux et libère toutes les ressources.
 * Implique file_capture_signal_stop si non encore appelé. */
CASTOR_CORE_API void file_capture_destroy(FileCaptureContext** ctx);

/* Bloquant — prochain frame vidéo BGRA depuis la queue.
 * Retourne NULL si le FileCapture est arrêté ou si le fichier est épuisé. */
CASTOR_CORE_API AVFrame* file_capture_next_video_frame(FileCaptureContext* ctx);

/* Bloquant — prochain frame audio FLTP 48 kHz stéréo depuis la queue.
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
