#pragma once
#include "castor_api.h"
#include "VideoCapture.h"
#include "AudioCapture.h"

#ifdef __cplusplus
extern "C" {
#endif

/* Nombre maximum de streams simultanees */
#define CASTOR_MAX_STREAMS 8

/* Configuration d'un stream : source video, source audio, fichier de sortie */
typedef struct {
    CaptureSourceInfo video_src;        /* source video choisie (ecran, fenetre, camera) */
    AudioSourceInfo   audio_src;        /* source audio choisie (loopback, micro, etc.)  */
    char              output_path[512]; /* chemin du fichier .mp4 de sortie              */
} StreamConfig;

/* Configuration globale du recorder */
typedef struct {
    StreamConfig streams[CASTOR_MAX_STREAMS]; /* configuration de chaque stream       */
    int          num_streams;                 /* nombre de streams actifs (>= 1)      */
    int          fps;                         /* frequence d'images cible (tous streams) */
} RecorderConfig;

typedef struct CastorRecorder CastorRecorder;  /* handle opaque */

/* Alloue un recorder et copie la configuration.
 * Ne demarre pas la capture ni l'encodage.
 *
 * config : parametres de l'enregistrement (num_streams >= 1)
 *
 * Retourne un pointeur vers le recorder alloue, NULL en cas d'erreur. */
CASTOR_CORE_API CastorRecorder* recorder_create (const RecorderConfig* config);

/* Initialise tous les streams (captures, encodeurs, muxers) puis lance les threads :
 *   - par stream : thread capture video, thread encodage video, thread audio
 * Non-bloquant : retourne des que tous les threads sont demarres.
 *
 * rec : recorder cree via recorder_create
 *
 * Retourne 0 si succes, -1 en cas d'erreur (ressources liberees automatiquement). */
CASTOR_CORE_API int             recorder_start  (CastorRecorder* rec);

/* Signale l'arret a tous les threads, attend leur terminaison (timeout 5s par thread),
 * flush les encodeurs, ecrit les trailers MP4 et libere toutes les ressources.
 * Bloquant jusqu'a la fin du cleanup.
 *
 * rec : recorder en cours d'enregistrement */
CASTOR_CORE_API void            recorder_stop   (CastorRecorder* rec);

/* Libere la memoire du recorder.
 * Doit etre appele apres recorder_stop.
 *
 * rec : recorder a detruire */
CASTOR_CORE_API void            recorder_destroy(CastorRecorder* rec);

/* Change la source video du stream indique en cours d'enregistrement.
 * La source audio et le fichier de sortie ne changent pas.
 * Si les dimensions de la nouvelle source different, le contexte de conversion
 * est reajuste automatiquement (la resolution du fichier de sortie reste inchangee).
 *
 * rec          : recorder en cours d'enregistrement
 * stream_index : index du stream cible (0 .. num_streams-1)
 * new_src      : nouvelle source video a capturer
 *
 * Retourne 0 si succes, -1 en cas d'erreur. */
CASTOR_CORE_API int             recorder_switch_video_source(CastorRecorder*          rec,
                                                              int                      stream_index,
                                                              const CaptureSourceInfo* new_src);

#ifdef __cplusplus
}
#endif
