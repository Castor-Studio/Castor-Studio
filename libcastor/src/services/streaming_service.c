#include "services/streaming_service.h"

#include <stdio.h>
#include <string.h>

/* Declarations des fonctions internes a chaque service */
int twitch_service_get_url (const char* stream_key, char* url_out, int url_len);
int youtube_service_get_url(const char* stream_key, char* url_out, int url_len);

/* ------------------------------------------------------------------ */

CASTOR_CORE_API int streaming_service_get_url(CastorServiceType  type,
                                               const char*        stream_key,
                                               char*              url_out,
                                               int                url_len)
{
    if (!stream_key || !url_out || url_len <= 0) return -1;

    switch (type) {
    case CASTOR_SERVICE_CUSTOM:
        /* stream_key contient l'URL complete */
        if ((int)strlen(stream_key) + 1 > url_len) {
            fprintf(stderr, "[StreamingService] buffer trop petit pour l'URL custom\n");
            return -1;
        }
        snprintf(url_out, url_len, "%s", stream_key);
        return 0;

    case CASTOR_SERVICE_TWITCH:
        return twitch_service_get_url(stream_key, url_out, url_len);

    case CASTOR_SERVICE_YOUTUBE:
        return youtube_service_get_url(stream_key, url_out, url_len);

    default:
        fprintf(stderr, "[StreamingService] type inconnu : %d\n", (int)type);
        return -1;
    }
}

CASTOR_CORE_API const char* streaming_service_name(CastorServiceType type)
{
    switch (type) {
    case CASTOR_SERVICE_CUSTOM:  return "Custom";
    case CASTOR_SERVICE_TWITCH:  return "Twitch";
    case CASTOR_SERVICE_YOUTUBE: return "YouTube Live";
    default:                     return "Inconnu";
    }
}
