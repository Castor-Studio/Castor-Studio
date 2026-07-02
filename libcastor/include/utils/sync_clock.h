#pragma once

#include "castor_api.h"
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/*
 * Horloge de reference partagee par toutes les sources fichier (preview ET
 * recorder), pour que deux scenes utilisant des fichiers differents restent
 * calees l'une sur l'autre meme quand leur decodeur est detruit/recree a
 * chaque changement de scene (voir video_capture_init_file / file_capture_create).
 *
 * Initialisee paresseusement au premier appel, pour toute la duree de vie du
 * process. Comme les sources en boucle ne dependent que de "elapsed % duree",
 * l'instant exact de l'initialisation n'a pas d'importance : seul le
 * dephasage relatif entre sources compte.
 */
CASTOR_CORE_API int64_t castor_file_sync_epoch_us(void);

#ifdef __cplusplus
}
#endif
