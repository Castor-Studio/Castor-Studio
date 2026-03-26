#include "services/streaming_service.h"

#include <windows.h>
#include <winhttp.h>
#include <stdio.h>
#include <string.h>
#include <stdlib.h>

#pragma comment(lib, "winhttp.lib")

#define TWITCH_INGEST_HOST  L"ingest.twitch.tv"
#define TWITCH_INGEST_PATH  L"/ingests"
#define TWITCH_TARGET_ID    1          /* _id: 1 = serveur optimal / le plus proche */
#define HTTP_BUFFER_SIZE    65536

/* ------------------------------------------------------------------ *
 *  Fetch HTTPS — retourne le body dans un buffer alloue par calloc.
 *  L'appelant doit free() le resultat.
 * ------------------------------------------------------------------ */
static char* fetch_ingests(void)
{
    HINTERNET session = WinHttpOpen(
        L"CastorApp/1.0",
        WINHTTP_ACCESS_TYPE_DEFAULT_PROXY,
        WINHTTP_NO_PROXY_NAME,
        WINHTTP_NO_PROXY_BYPASS, 0);
    if (!session) {
        fprintf(stderr, "[Twitch] WinHttpOpen failed: %lu\n", GetLastError());
        return NULL;
    }

    HINTERNET connect = WinHttpConnect(session, TWITCH_INGEST_HOST,
                                       INTERNET_DEFAULT_HTTPS_PORT, 0);
    if (!connect) {
        fprintf(stderr, "[Twitch] WinHttpConnect failed: %lu\n", GetLastError());
        WinHttpCloseHandle(session);
        return NULL;
    }

    HINTERNET request = WinHttpOpenRequest(
        connect, L"GET", TWITCH_INGEST_PATH,
        NULL, WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES,
        WINHTTP_FLAG_SECURE);
    if (!request) {
        fprintf(stderr, "[Twitch] WinHttpOpenRequest failed: %lu\n", GetLastError());
        WinHttpCloseHandle(connect);
        WinHttpCloseHandle(session);
        return NULL;
    }

    if (!WinHttpSendRequest(request,
            WINHTTP_NO_ADDITIONAL_HEADERS, 0,
            WINHTTP_NO_REQUEST_DATA, 0, 0, 0) ||
        !WinHttpReceiveResponse(request, NULL))
    {
        fprintf(stderr, "[Twitch] WinHttpSendRequest/ReceiveResponse failed: %lu\n", GetLastError());
        WinHttpCloseHandle(request);
        WinHttpCloseHandle(connect);
        WinHttpCloseHandle(session);
        return NULL;
    }

    char*  body     = (char*)calloc(1, HTTP_BUFFER_SIZE);
    DWORD  total    = 0;
    DWORD  bytes_read;

    if (!body) goto cleanup;

    do {
        DWORD available = 0;
        WinHttpQueryDataAvailable(request, &available);
        if (available == 0) break;
        if (total + available >= HTTP_BUFFER_SIZE - 1) available = HTTP_BUFFER_SIZE - 1 - total;

        WinHttpReadData(request, body + total, available, &bytes_read);
        total += bytes_read;
    } while (bytes_read > 0 && total < HTTP_BUFFER_SIZE - 1);

    body[total] = '\0';

cleanup:
    WinHttpCloseHandle(request);
    WinHttpCloseHandle(connect);
    WinHttpCloseHandle(session);
    return body;
}

/* ------------------------------------------------------------------ *
 *  Parse JSON minimal — cherche l'entree avec "_id": <target_id>
 *  et en extrait url_template.
 *
 *  Format attendu (Twitch ingests API) :
 *    { "ingests": [ { "_id": 1, ..., "url_template": "rtmp://.../{stream_key}", ... }, ... ] }
 * ------------------------------------------------------------------ */
static int parse_url_template(const char* json, int target_id,
                               char* tmpl_out, int tmpl_len)
{
    const char* p = json;

    while ((p = strstr(p, "\"_id\"")) != NULL) {
        p += 5; /* saute "_id" */
        while (*p == ' ' || *p == ':' || *p == '\t') p++;

        int id = atoi(p);
        if (id != target_id) { p++; continue; }

        /* Trouve url_template dans le meme objet JSON */
        const char* block_end = strstr(p, "}");
        if (!block_end) break;

        const char* key = strstr(p, "\"url_template\"");
        if (!key || key > block_end) { p++; continue; }

        key = strchr(key, ':');
        if (!key) break;
        key++;
        while (*key == ' ' || *key == '\t') key++;
        if (*key != '"') break;
        key++; /* saute le " ouvrant */

        const char* end = strchr(key, '"');
        if (!end) break;

        int len = (int)(end - key);
        if (len >= tmpl_len) len = tmpl_len - 1;
        memcpy(tmpl_out, key, len);
        tmpl_out[len] = '\0';
        return 0;
    }

    fprintf(stderr, "[Twitch] url_template introuvable pour _id=%d\n", target_id);
    return -1;
}

/* ------------------------------------------------------------------ *
 *  API publique
 * ------------------------------------------------------------------ */
int twitch_service_get_url(const char* stream_key, char* url_out, int url_len)
{
    fprintf(stderr, "[Twitch] Recuperation du serveur ingest (_id=%d)...\n", TWITCH_TARGET_ID);

    char* json = fetch_ingests();
    if (!json) return -1;

    char url_template[512] = {0};
    if (parse_url_template(json, TWITCH_TARGET_ID, url_template, sizeof(url_template)) < 0) {
        free(json);
        return -1;
    }
    free(json);

    /* Remplace {stream_key} par la vraie cle */
    const char* placeholder = strstr(url_template, "{stream_key}");
    if (!placeholder) {
        fprintf(stderr, "[Twitch] placeholder {stream_key} absent du template: %s\n", url_template);
        return -1;
    }

    int prefix_len = (int)(placeholder - url_template);
    int key_len    = (int)strlen(stream_key);
    int suffix_len = (int)strlen(placeholder + 12); /* 12 = len("{stream_key}") */

    if (prefix_len + key_len + suffix_len + 1 > url_len) {
        fprintf(stderr, "[Twitch] buffer url_out trop petit\n");
        return -1;
    }

    memcpy(url_out,                         url_template, prefix_len);
    memcpy(url_out + prefix_len,            stream_key,   key_len);
    memcpy(url_out + prefix_len + key_len,  placeholder + 12, suffix_len + 1);

    fprintf(stderr, "[Twitch] URL ingest : %s\n", url_out);
    return 0;
}
