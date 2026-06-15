#include "services/streaming_service.h"

#include <stdio.h>
#include <string.h>

/* YouTube Live RTMP endpoint.
 * Format : rtmps://a.rtmp.youtube.com:443/live2/<stream_key>
 *
 * Note : YouTube accepte aussi rtmp:// (port 1935) mais recommande
 * rtmps pour la securite. */
#define YOUTUBE_RTMP_PREFIX "rtmps://a.rtmp.youtube.com:443/live2/"

int youtube_service_get_url(const char* stream_key, char* url_out, int url_len)
{
    int needed = (int)(strlen(YOUTUBE_RTMP_PREFIX) + strlen(stream_key) + 1);
    if (needed > url_len) {
        fprintf(stderr, "[YouTube] buffer url_out trop petit\n");
        return -1;
    }

    snprintf(url_out, url_len, "%s%s", YOUTUBE_RTMP_PREFIX, stream_key);
    fprintf(stderr, "[YouTube] URL : %s\n", url_out);
    return 0;
}
