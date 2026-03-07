#pragma once
#include "castor_api.h"
#include "VideoCapture.h"
#include "AudioCapture.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef struct {
    CaptureSourceInfo video_src;   /* source vidéo choisie (écran, fenêtre, caméra) */
    AudioSourceInfo   audio_src;   /* source audio choisie (loopback, micro, etc.)  */
    char              video_path[512];  /* chemin du fichier .h264 de sortie */
    char              audio_path[512];  /* chemin du fichier .aac de sortie  */
    int               fps;              /* fréquence d'images cible           */
} RecorderConfig;

typedef struct CastorRecorder CastorRecorder;  /* handle opaque */

/* Alloue un recorder et copie la configuration.
 * Ne démarre pas la capture ni l'encodage.
 *
 * config : paramètres de l'enregistrement
 *
 * Retourne un pointeur vers le recorder alloué, NULL en cas d'erreur. */
CASTOR_CORE_API CastorRecorder* recorder_create (const RecorderConfig* config);

/* Initialise les captures vidéo/audio, les encodeurs, puis lance les 3 threads :
 *   - thread capture vidéo  : alimente le dernier frame disponible
 *   - thread encodage vidéo : boucle fps strict, encode vers le fichier .h264
 *   - thread audio          : capture + encode vers le fichier .aac
 * Non-bloquant : retourne dès que les threads sont démarrés.
 *
 * rec : recorder créé via recorder_create
 *
 * Retourne 0 si succès, -1 en cas d'erreur (ressources libérées automatiquement). */
CASTOR_CORE_API int             recorder_start  (CastorRecorder* rec);

/* Signale l'arrêt aux 3 threads, attend leur terminaison (timeout 5s),
 * flush les encodeurs, écrit les fichiers de sortie et libère toutes les ressources.
 * Bloquant jusqu'à la fin du cleanup.
 *
 * rec : recorder en cours d'enregistrement */
CASTOR_CORE_API void            recorder_stop   (CastorRecorder* rec);

/* Libère la mémoire du recorder.
 * Doit être appelé après recorder_stop.
 *
 * rec : recorder à détruire */
CASTOR_CORE_API void            recorder_destroy(CastorRecorder* rec);

#ifdef __cplusplus
}
#endif
