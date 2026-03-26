#pragma once

#include "castor_api.h"

#ifdef __cplusplus
extern "C" {
#endif

/* ================================================================== *
 *  CastorServiceType — services de streaming supportes
 * ================================================================== */
typedef enum {
    CASTOR_SERVICE_CUSTOM  = 0,  /* URL RTMP/RTMPS complete fournie manuellement */
    CASTOR_SERVICE_TWITCH  = 1,  /* Twitch : fetch ingest.twitch.tv + cle         */
    CASTOR_SERVICE_YOUTUBE = 2,  /* YouTube Live : URL fixe + cle                 */
} CastorServiceType;

/* ================================================================== *
 *  streaming_service_get_url
 *
 *  Construit l'URL RTMP complete a partir du service et de la cle.
 *
 *  type       : service cible
 *  stream_key : cle de stream (ex: "live_152304002_xxx" pour Twitch)
 *               Pour CASTOR_SERVICE_CUSTOM, passer l'URL complete ici.
 *  url_out    : buffer de sortie
 *  url_len    : taille du buffer (recommande : 512)
 *
 *  Retourne 0 si succes, -1 en cas d'erreur.
 *
 *  Note Twitch : effectue un appel HTTPS a ingest.twitch.tv/ingests
 *  pour recuperer le serveur le plus proche (_id: 1 = optimal).
 *  Necessite une connexion internet.
 * ================================================================== */
CASTOR_CORE_API int streaming_service_get_url(CastorServiceType  type,
                                               const char*        stream_key,
                                               char*              url_out,
                                               int                url_len);

/* Retourne le nom affichable du service (ex: "Twitch"). */
CASTOR_CORE_API const char* streaming_service_name(CastorServiceType type);

#ifdef __cplusplus
}
#endif
