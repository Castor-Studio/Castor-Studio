#pragma once

#include "castor_api.h"

#ifdef _WIN32
#include <windows.h>
#endif

#ifdef __cplusplus
#include <stdbool.h>
extern "C" {
#endif

#include <libavutil/frame.h>

/* ------------------------------------------------------------------ *
 *  Types existants
 * ------------------------------------------------------------------ */

typedef struct {
    void* hwnd;
    char  title[256];
} WindowInfo;

typedef struct {
    void*  monitor;
    char   name[32];
    RECT   rect;
} MonitorInfo;

typedef struct {
    void* internal;
    int   width;
    int   height;
} VideoCaptureContext;

/* ------------------------------------------------------------------ *
 *  Source unifiee (ecran, fenêtre, camera)
 * ------------------------------------------------------------------ */

typedef enum {
    CAPTURE_SOURCE_WINDOW  = 0,
    CAPTURE_SOURCE_MONITOR = 1,
    CAPTURE_SOURCE_CAMERA  = 2,
} CaptureSourceType;

typedef struct {
    char              label[256];
    CaptureSourceType type;

    void*             hwnd;
    void*             hmonitor;

    char              symbolic_link[512];
    int               index;
} CaptureSourceInfo;

/* ------------------------------------------------------------------ *
 *  API
 * ------------------------------------------------------------------ */

/* Enumère les fenêtres visibles du bureau (titre non vide, non toolwindow).
 * out       : tableau de WindowInfo à remplir
 * max_count : taille du tableau
 * Retourne le nombre de fenêtres trouvées. */
CASTOR_CORE_API int capture_list_windows (WindowInfo*       out, int max_count);

/* Enumère les moniteurs connectés.
 * out       : tableau de MonitorInfo à remplir
 * max_count : taille du tableau
 * Retourne le nombre de moniteurs trouvés. */
CASTOR_CORE_API int capture_list_monitors(MonitorInfo*      out, int max_count);

/* Liste toutes les sources vidéo disponibles dans l'ordre :
 * moniteurs, fenêtres visibles, puis caméras (Media Foundation).
 * out       : tableau de CaptureSourceInfo à remplir
 * max_count : taille du tableau
 * Retourne le nombre total de sources trouvées. */
CASTOR_CORE_API int video_capture_list_sources(CaptureSourceInfo* out, int max_count);

/* Initialise la capture WGC (Windows Graphics Capture) sur une fenêtre.
 * ctx  : contexte à initialiser
 * hwnd : handle de la fenêtre cible
 * Retourne 0 si succès, -1 en cas d'erreur. */
CASTOR_CORE_API int video_capture_init_window  (VideoCaptureContext* ctx, void* hwnd);

/* Initialise la capture WGC sur un moniteur.
 * ctx      : contexte à initialiser
 * hmonitor : handle du moniteur cible
 * Retourne 0 si succès, -1 en cas d'erreur. */
CASTOR_CORE_API int video_capture_init_monitor (VideoCaptureContext* ctx, void* hmonitor);

/* Initialise la capture Media Foundation sur une webcam.
 * ctx           : contexte à initialiser
 * symbolic_link : identifiant unique du device vidéo (UTF-8)
 * Retourne 0 si succès, -1 en cas d'erreur. */
CASTOR_CORE_API int video_capture_init_camera  (VideoCaptureContext* ctx, const char* symbolic_link);

/* Dispatch vers init_window, init_monitor ou init_camera selon src->type.
 * ctx : contexte à initialiser
 * src : source sélectionnée via video_capture_list_sources
 * Retourne 0 si succès, -1 en cas d'erreur. */
CASTOR_CORE_API int video_capture_init_source  (VideoCaptureContext* ctx, CaptureSourceInfo* src);

/* Capture le prochain frame disponible.
 * Bloquant jusqu'à ~33ms (WGC) ou jusqu'à ReadSample (Media Foundation).
 * ctx : contexte de capture initialisé
 * Retourne un AVFrame* BGRA alloué, à libérer avec av_frame_free(). NULL en cas d'erreur. */
CASTOR_CORE_API AVFrame* video_capture_next_frame(VideoCaptureContext* ctx);

/* Ferme la session de capture et libère toutes les ressources internes.
 * ctx : contexte à nettoyer */
CASTOR_CORE_API void     video_capture_cleanup   (VideoCaptureContext* ctx);

/* Affiche la liste des sources vidéo dans le terminal et invite l'utilisateur
 * à saisir un index. Remplit *out avec la source choisie.
 * out : source sélectionnée par l'utilisateur
 * Retourne 0 si succès, -1 si le choix est invalide. */
CASTOR_CORE_API int video_capture_select_source_cli(CaptureSourceInfo* out);

/* Génère un frame BGRA de test (contenu indéfini). Debug uniquement. */
CASTOR_CORE_API AVFrame* capture_dummy_video_frame(void);

/* Enregistre le type "video_capture" dans le registre de sources.
 * Doit etre appele apres source_registry_init().
 * Retourne true si succes. */
CASTOR_CORE_API bool video_capture_module_load(void);

#ifdef __cplusplus
}
#endif
