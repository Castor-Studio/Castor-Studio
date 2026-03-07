#pragma once
#include "castor_api.h"
#include "VideoCapture.h"
#include "AudioCapture.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef struct {
    CaptureSourceInfo video_src;        /* source video choisie (ecran, fenetre, camera)  */
    AudioSourceInfo   audio_src;        /* source audio choisie (loopback, micro, etc.)   */
    char              output_path[512]; /* chemin du fichier .mp4 de sortie               */
    int               fps;              /* frequence d'images cible                       */
} RecorderConfig;

typedef struct CastorRecorder CastorRecorder;  /* handle opaque */

/* Alloue un recorder et copie la configuration.
 * Ne démarre pas la capture ni l'encodage.
 *
 * config : paramètres de l'enregistrement
 *
 * Retourne un pointeur vers le recorder alloué, NULL en cas d'erreur. */
CASTOR_CORE_API CastorRecorder* recorder_create (const RecorderConfig* config);

/* Initialise les captures video/audio, les encodeurs, le muxer MP4, puis lance les 3 threads :
 *   - thread capture video  : alimente le dernier frame disponible
 *   - thread encodage video : boucle fps strict, encode vers le container MP4
 *   - thread audio          : capture + encode vers le container MP4
 * Non-bloquant : retourne des que les threads sont demarres.
 *
 * rec : recorder créé via recorder_create
 *
 * Retourne 0 si succès, -1 en cas d'erreur (ressources libérées automatiquement). */
CASTOR_CORE_API int             recorder_start  (CastorRecorder* rec);

/* Signale l'arret aux 3 threads, attend leur terminaison (timeout 5s),
 * flush les encodeurs, ecrit le trailer MP4 et libere toutes les ressources.
 * Bloquant jusqu'a la fin du cleanup.
 *
 * rec : recorder en cours d'enregistrement */
CASTOR_CORE_API void            recorder_stop   (CastorRecorder* rec);

/* Libere la memoire du recorder.
 * Doit etre appele apres recorder_stop.
 *
 * rec : recorder à détruire */
CASTOR_CORE_API void            recorder_destroy(CastorRecorder* rec);

#ifdef __cplusplus
}
#endif
