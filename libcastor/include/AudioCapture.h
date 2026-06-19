#pragma once
#include "castor_api.h"
#include <libavutil/frame.h>

/* ------------------------------------------------------------------ *
 *  Types legacy (conserves)
 * ------------------------------------------------------------------ */
typedef enum {
    AUDIO_DEVICE_CAPTURE  = 0,
    AUDIO_DEVICE_LOOPBACK = 1,
} AudioDeviceType;

typedef struct {
    char            name[256];
    AudioDeviceType type;
    int             index;
} AudioDeviceInfo;

/* ------------------------------------------------------------------ *
 *  Source unifiee — miroir de CaptureSourceInfo côte video
 * ------------------------------------------------------------------ */
typedef enum {
    AUDIO_SOURCE_LOOPBACK_GLOBAL = 0,  /* loopback système entier        */
    AUDIO_SOURCE_LOOPBACK_WINDOW = 1,  /* loopback fenêtre (best-effort) */
    AUDIO_SOURCE_MICROPHONE      = 2,  /* micro USB/jack/bluetooth       */
    AUDIO_SOURCE_CAMERA_MIC      = 3,  /* micro integre camera           */
    AUDIO_SOURCE_FILE            = 4,  /* fichier local — chemin dans device_id, index=1 si loop */
} AudioSourceType;

typedef struct {
    char            label[256];
    AudioSourceType type;

    /* LOOPBACK_WINDOW */
    void*           hwnd;

    /* MICROPHONE / CAMERA_MIC — ID WASAPI unique */
    char            device_id[512];

    int             index;
} AudioSourceInfo;

/* ------------------------------------------------------------------ *
 *  Contexte de capture
 * ------------------------------------------------------------------ */
typedef struct {
    void* internal;
    int   sample_rate;
    int   channels;
} AudioCaptureContext;

#ifdef __cplusplus
extern "C" {
#endif

/* Capture audio uniquement du process associe au hwnd donne,
 * via le Windows Process Loopback API (Windows 10 2004+, build 19041).
 * Retourne 0 si succes, -1 si erreur. */
CASTOR_CORE_API int audio_capture_init_process_loopback(AudioCaptureContext* ctx, void* hwnd);

/* Liste les devices audio WASAPI bruts (legacy, microphones + loopback).
 * out       : tableau de AudioDeviceInfo à remplir
 * max_count : taille du tableau
 * Retourne le nombre de devices trouvés. */
CASTOR_CORE_API int capture_list_audio_devices(AudioDeviceInfo*  out, int max_count);

/* Liste toutes les sources audio disponibles dans l'ordre :
 * loopback global, loopback par fenêtre, microphones, micros caméra.
 * out       : tableau de AudioSourceInfo à remplir
 * max_count : taille du tableau
 * Retourne le nombre total de sources trouvées. */
CASTOR_CORE_API int audio_capture_list_sources(AudioSourceInfo*  out, int max_count);

/* Dispatch vers l'init WASAPI approprié selon src->type.
 * ctx : contexte à initialiser
 * src : source sélectionnée via audio_capture_list_sources
 * Retourne 0 si succès, -1 en cas d'erreur. */
CASTOR_CORE_API int      audio_capture_init_source (AudioCaptureContext* ctx, AudioSourceInfo* src);

/* Initialise la capture en loopback sur le device render par défaut (legacy).
 * ctx  : contexte à initialiser
 * hwnd : ignoré pour l'instant (loopback global uniquement)
 * Retourne 0 si succès, -1 en cas d'erreur. */
CASTOR_CORE_API int      audio_capture_init        (AudioCaptureContext* ctx, void* hwnd);

/* Capture le prochain paquet audio disponible via WASAPI.
 * Bloque jusqu'à ~20ms si le buffer est vide.
 * ctx : contexte de capture initialisé
 * Retourne un AVFrame* FLTP alloué (max stéréo), à libérer avec av_frame_free(). NULL si timeout. */
CASTOR_CORE_API AVFrame* audio_capture_next_frame  (AudioCaptureContext* ctx);

/* Arrête le client WASAPI et libère toutes les ressources internes.
 * ctx : contexte à nettoyer */
CASTOR_CORE_API void     audio_capture_cleanup     (AudioCaptureContext* ctx);

/* Affiche la liste des sources audio dans le terminal et invite l'utilisateur
 * à saisir un index. Remplit *out avec la source choisie.
 * out : source sélectionnée par l'utilisateur
 * Retourne 0 si succès, -1 si le choix est invalide. */
CASTOR_CORE_API int audio_capture_select_source_cli(AudioSourceInfo* out);

/* Génère un frame sinusoïdal de test (440Hz, 1024 samples, 48kHz stéréo). Debug uniquement. */
CASTOR_CORE_API AVFrame* capture_dummy_audio_frame(void);

/* Enregistre le type "audio_capture" dans le registre de sources.
 * Doit etre appele apres source_registry_init().
 * Retourne true si succes. */
CASTOR_CORE_API bool audio_capture_module_load(void);

#ifdef __cplusplus
}
#endif
