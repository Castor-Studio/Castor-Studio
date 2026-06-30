#define INITGUID
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <functiondiscoverykeys_devpkey.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <math.h>
#include <stdio.h>
#include <string.h>
#include <pthread.h>
#include "AudioCapture.h"
#include "source/source.h"
#include "source/source_registry.h"

#include <libavformat/avformat.h>
#include <libavcodec/avcodec.h>
#include <libswresample/swresample.h>
#include <libavutil/opt.h>

#pragma comment(lib, "mf.lib")
#pragma comment(lib, "mfplat.lib")
#pragma comment(lib, "mfreadwrite.lib")
#pragma comment(lib, "mfuuid.lib")
#pragma comment(lib, "ole32.lib")

/* ------------------------------------------------------------------ *
 *  GUIDs definis manuellement (coherent avec le reste du fichier)
 * ------------------------------------------------------------------ */
const CLSID local_CLSID_MMDeviceEnumerator = {
    0xBCDE0395, 0xE52F, 0x467C,
    {0x8E, 0x3D, 0xC4, 0x57, 0x92, 0x91, 0x69, 0x2E}
};
const IID local_IID_IMMDeviceEnumerator = {
    0xA95664D2, 0x9614, 0x4F35,
    {0xA7, 0x46, 0xDE, 0x8D, 0xB6, 0x36, 0x17, 0xE6}
};
/* IAudioClient  {1CB9AD4C-DBFA-4C32-B178-C2F568A703B2} */
static const IID local_IID_IAudioClient = {
    0x1CB9AD4C, 0xDBFA, 0x4C32,
    {0xB1, 0x78, 0xC2, 0xF5, 0x68, 0xA7, 0x03, 0xB2}
};
/* IAudioCaptureClient  {C8ADBD64-E71E-48a0-A4DE-185C395CD317} */
static const IID local_IID_IAudioCaptureClient = {
    0xC8ADBD64, 0xE71E, 0x48A0,
    {0xA4, 0xDE, 0x18, 0x5C, 0x39, 0x5C, 0xD3, 0x17}
};

/* ------------------------------------------------------------------ *
 *  Contexte interne (opaque côte .h)
 *  IMPORTANT : is_file doit rester le premier membre de TOUTES les
 *  structs internes pour permettre le dispatch générique.
 * ------------------------------------------------------------------ */
typedef struct {
    int                   is_file;    /* 0 = WASAPI — initialisé à 0 par calloc */
    IMMDeviceEnumerator*  enumerator;
    IMMDevice*            device;
    IAudioClient*         client;
    IAudioCaptureClient*  capture;
    WAVEFORMATEX*         wave_fmt;
    int                   sample_rate;
    int                   channels;
} AudioCaptureInternal;

typedef struct {
    int                is_file;        /* 1 = fichier FFmpeg */
    AVFormatContext*   fmt_ctx;
    AVCodecContext*    codec_ctx;
    SwrContext*        swr_ctx;
    int                stream_idx;
    int                sample_rate;
    int                channels;
    int                loop;
    int64_t            next_pts;       /* compteur de samples monotone — ne se réinitialise JAMAIS */
    int64_t            start_us;       /* horloge murale au démarrage (µs) pour le rate-limiting */
} AudioFileCaptureInternal;

/* Forward declaration — implémentation après audio_capture_cleanup */
static AVFrame* next_frame_file_audio(AudioFileCaptureInternal* internal);

/* ------------------------------------------------------------------ *
 *  Helpers format WAVEFORMATEX → AVFrame FLTP
 * ------------------------------------------------------------------ */
static int is_float_fmt(const WAVEFORMATEX* fmt) {
    if (fmt->wFormatTag == WAVE_FORMAT_IEEE_FLOAT) return 1;
    if (fmt->wFormatTag == WAVE_FORMAT_EXTENSIBLE) {
        /* SubFormat KSDATAFORMAT_SUBTYPE_IEEE_FLOAT */
        static const GUID float_guid = {
            0x00000003, 0x0000, 0x0010,
            {0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71}
        };
        const WAVEFORMATEXTENSIBLE* ext =
            (const WAVEFORMATEXTENSIBLE*)fmt;
        return memcmp(&ext->SubFormat, &float_guid, sizeof(GUID)) == 0;
    }
    return 0;
}

static AVFrame* wasapi_to_avframe(const BYTE* data, UINT32 frames, const WAVEFORMATEX* fmt) {
    AVFrame* frame = av_frame_alloc();
    if (!frame) return NULL;

    /* Limiter à stéréo même si le device rapporte plus de 2 canaux */
    const int ch = (fmt->nChannels > 2) ? 2 : (int)fmt->nChannels;

    frame->format      = AV_SAMPLE_FMT_FLTP;
    frame->sample_rate = (int)fmt->nSamplesPerSec;
    frame->nb_samples  = (int)frames;
    av_channel_layout_default(&frame->ch_layout, ch);  /* toujours stéréo max */

    if (av_frame_get_buffer(frame, 0) < 0) {
        av_frame_free(&frame);
        return NULL;
    }

    const int src_ch = (int)fmt->nChannels;  /* canaux réels de WASAPI */

    if (is_float_fmt(fmt)) {
        const float* src = (const float*)data;
        for (int c = 0; c < ch; c++) {
            float* dst = (float*)frame->data[c];
            for (UINT32 s = 0; s < frames; s++)
                dst[s] = src[s * src_ch + c];  /* prend ch 0 et 1, ignore 2 et 3 */
        }
    } else {
        const int16_t* src = (const int16_t*)data;
        for (int c = 0; c < ch; c++) {
            float* dst = (float*)frame->data[c];
            for (UINT32 s = 0; s < frames; s++)
                dst[s] = src[s * src_ch + c] / 32768.0f;
        }
    }

    return frame;
}

/* ------------------------------------------------------------------ *
 *  Listing devices (dejà present, conserve tel quel)
 * ------------------------------------------------------------------ */
CASTOR_CORE_API int capture_list_audio_devices(AudioDeviceInfo* out, int max_count) {
    IMMDeviceEnumerator* enumerator = NULL;
    IMMDeviceCollection* collection = NULL;
    int count = 0;

    CoInitializeEx(NULL, COINIT_MULTITHREADED);

    HRESULT hr = CoCreateInstance(
        &local_CLSID_MMDeviceEnumerator, NULL, CLSCTX_ALL,
        &local_IID_IMMDeviceEnumerator, (void**)&enumerator);
    if (FAILED(hr)) return 0;

    /* Microphones */
    hr = enumerator->lpVtbl->EnumAudioEndpoints(
        enumerator, eCapture, DEVICE_STATE_ACTIVE, &collection);
    if (SUCCEEDED(hr)) {
        UINT device_count = 0;
        collection->lpVtbl->GetCount(collection, &device_count);
        for (UINT i = 0; i < device_count && count < max_count; i++) {
            IMMDevice* device = NULL;
            collection->lpVtbl->Item(collection, i, &device);
            IPropertyStore* props = NULL;
            device->lpVtbl->OpenPropertyStore(device, STGM_READ, &props);
            PROPVARIANT name; PropVariantInit(&name);
            props->lpVtbl->GetValue(props, &PKEY_Device_FriendlyName, &name);
            WideCharToMultiByte(CP_UTF8, 0, name.pwszVal, -1,
                out[count].name, 256, NULL, NULL);
            out[count].type  = AUDIO_DEVICE_CAPTURE;
            out[count].index = i;
            count++;
            PropVariantClear(&name);
            props->lpVtbl->Release(props);
            device->lpVtbl->Release(device);
        }
        collection->lpVtbl->Release(collection);
    }

    /* Loopback */
    hr = enumerator->lpVtbl->EnumAudioEndpoints(
        enumerator, eRender, DEVICE_STATE_ACTIVE, &collection);
    if (SUCCEEDED(hr)) {
        UINT device_count = 0;
        collection->lpVtbl->GetCount(collection, &device_count);
        for (UINT i = 0; i < device_count && count < max_count; i++) {
            IMMDevice* device = NULL;
            collection->lpVtbl->Item(collection, i, &device);
            IPropertyStore* props = NULL;
            device->lpVtbl->OpenPropertyStore(device, STGM_READ, &props);
            PROPVARIANT name; PropVariantInit(&name);
            props->lpVtbl->GetValue(props, &PKEY_Device_FriendlyName, &name);
            char base_name[200] = {};
            WideCharToMultiByte(CP_UTF8, 0, name.pwszVal, -1,
                base_name, sizeof(base_name), NULL, NULL);
            snprintf(out[count].name, 256, "[Loopback] %s", base_name);
            out[count].type  = AUDIO_DEVICE_LOOPBACK;
            out[count].index = i;
            count++;
            PropVariantClear(&name);
            props->lpVtbl->Release(props);
            device->lpVtbl->Release(device);
        }
        collection->lpVtbl->Release(collection);
    }

    enumerator->lpVtbl->Release(enumerator);
    return count;
}

/* ------------------------------------------------------------------ *
 *  audio_capture_init
 *  hwnd ignore pour l'instant — loopback sur le device render par defaut.
 *  (Le loopback par-process necessite ActivateAudioInterfaceAsync,
 *   C++ only — prevu pour une prochaine iteration.)
 * ------------------------------------------------------------------ */
CASTOR_CORE_API int audio_capture_init(AudioCaptureContext* ctx, void* hwnd) {
    (void)hwnd;   /* loopback global pour l'instant */

    CoInitializeEx(NULL, COINIT_MULTITHREADED);

    AudioCaptureInternal* internal =
        (AudioCaptureInternal*)calloc(1, sizeof(AudioCaptureInternal));
    if (!internal) return -1;

    HRESULT hr;

    /* 1. enumerateur */
    hr = CoCreateInstance(
        &local_CLSID_MMDeviceEnumerator, NULL, CLSCTX_ALL,
        &local_IID_IMMDeviceEnumerator, (void**)&internal->enumerator);
    if (FAILED(hr)) goto fail;

    /* 2. Device render par defaut (source du loopback) */
    hr = internal->enumerator->lpVtbl->GetDefaultAudioEndpoint(
        internal->enumerator, eRender, eConsole, &internal->device);
    if (FAILED(hr)) goto fail;

    /* 3. Activer IAudioClient */
    hr = internal->device->lpVtbl->Activate(
        internal->device, &local_IID_IAudioClient,
        CLSCTX_ALL, NULL, (void**)&internal->client);
    if (FAILED(hr)) goto fail;

    /* 4. Format du mix */
    hr = internal->client->lpVtbl->GetMixFormat(
        internal->client, &internal->wave_fmt);
    if (FAILED(hr)) goto fail;

    /* 5. Initialiser en mode loopback */
    hr = internal->client->lpVtbl->Initialize(
        internal->client,
        AUDCLNT_SHAREMODE_SHARED,
        AUDCLNT_STREAMFLAGS_LOOPBACK,
        10000000,   /* buffer 1s en unites 100ns */
        0,
        internal->wave_fmt,
        NULL);
    if (FAILED(hr)) goto fail;

    /* 6. IAudioCaptureClient */
    hr = internal->client->lpVtbl->GetService(
        internal->client, &local_IID_IAudioCaptureClient,
        (void**)&internal->capture);
    if (FAILED(hr)) goto fail;

    /* 7. Demarrer */
    internal->client->lpVtbl->Start(internal->client);

    internal->sample_rate = (int)internal->wave_fmt->nSamplesPerSec;
    internal->channels    = (int)internal->wave_fmt->nChannels;

    ctx->internal    = internal;
    ctx->sample_rate = internal->sample_rate;
    ctx->channels    = internal->channels;

    fprintf(stdout, "[AudioCapture] Loopback OK — %d Hz, %d ch\n",
            ctx->sample_rate, ctx->channels);
    return 0;

fail:
    fprintf(stderr, "[AudioCapture] Init echouee (hr=0x%lx)\n", hr);
    if (internal->capture)   internal->capture->lpVtbl->Release(internal->capture);
    if (internal->client)    internal->client->lpVtbl->Release(internal->client);
    if (internal->device)    internal->device->lpVtbl->Release(internal->device);
    if (internal->enumerator)internal->enumerator->lpVtbl->Release(internal->enumerator);
    if (internal->wave_fmt)  CoTaskMemFree(internal->wave_fmt);
    free(internal);
    return -1;
}

/* ------------------------------------------------------------------ *
 *  audio_capture_next_frame
 *  Retourne un AVFrame* FLTP, NULL si rien dans ~20ms.
 *  Le caller libère avec av_frame_free().
 * ------------------------------------------------------------------ */
CASTOR_CORE_API AVFrame* audio_capture_next_frame(AudioCaptureContext* ctx) {
    if (!ctx || !ctx->internal) return NULL;
    if (*(int*)ctx->internal == 1)
        return next_frame_file_audio((AudioFileCaptureInternal*)ctx->internal);
    AudioCaptureInternal* internal = (AudioCaptureInternal*)ctx->internal;

    for (int i = 0; i < 20; i++) {
        UINT32 frames_available = 0;
        HRESULT hr = internal->capture->lpVtbl->GetNextPacketSize(
            internal->capture, &frames_available);
        if (FAILED(hr)) return NULL;

        if (frames_available > 0) {
            BYTE*  data  = NULL;
            UINT32 count = 0;
            DWORD  flags = 0;

            hr = internal->capture->lpVtbl->GetBuffer(
                internal->capture, &data, &count, &flags, NULL, NULL);
            if (FAILED(hr)) return NULL;

            AVFrame* frame = NULL;

            if (flags & AUDCLNT_BUFFERFLAGS_SILENT) {
                /* Silence — generer un frame vide plutot que de skipper */
                frame = av_frame_alloc();
                frame->format      = AV_SAMPLE_FMT_FLTP;
                frame->sample_rate = internal->sample_rate;
                frame->nb_samples  = (int)count;
                av_channel_layout_default(&frame->ch_layout, internal->channels);
                av_frame_get_buffer(frame, 0);
                /* av_frame_get_buffer appelle av_malloc (pas av_mallocz) -> zero explicite */
                for (int ch = 0; ch < frame->ch_layout.nb_channels; ch++)
                    memset(frame->data[ch], 0, (int)count * sizeof(float));
            } else {
                frame = wasapi_to_avframe(data, count, internal->wave_fmt);
            }

            internal->capture->lpVtbl->ReleaseBuffer(internal->capture, count);
            return frame;
        }

        Sleep(1);
    }

    return NULL;
}

/* ------------------------------------------------------------------ *
 *  audio_capture_cleanup
 * ------------------------------------------------------------------ */
CASTOR_CORE_API void audio_capture_cleanup(AudioCaptureContext* ctx) {
    if (!ctx || !ctx->internal) return;
    if (*(int*)ctx->internal == 1) {
        AudioFileCaptureInternal* internal = (AudioFileCaptureInternal*)ctx->internal;
        if (internal->swr_ctx)    swr_free(&internal->swr_ctx);
        if (internal->codec_ctx)  avcodec_free_context(&internal->codec_ctx);
        if (internal->fmt_ctx)    avformat_close_input(&internal->fmt_ctx);
        free(internal);
        ctx->internal = NULL;
        return;
    }
    AudioCaptureInternal* internal = (AudioCaptureInternal*)ctx->internal;

    if (internal->client)    internal->client->lpVtbl->Stop(internal->client);
    if (internal->capture)   internal->capture->lpVtbl->Release(internal->capture);
    if (internal->client)    internal->client->lpVtbl->Release(internal->client);
    if (internal->device)    internal->device->lpVtbl->Release(internal->device);
    if (internal->enumerator)internal->enumerator->lpVtbl->Release(internal->enumerator);
    if (internal->wave_fmt)  CoTaskMemFree(internal->wave_fmt);

    free(internal);
    ctx->internal = NULL;
    CoUninitialize();
}

/* ------------------------------------------------------------------ *
 *  Helpers WASAPI internes
 * ------------------------------------------------------------------ */

/* Recupère le device_id WASAPI (wide) d'un IMMDevice → UTF-8 dans out */
static void get_device_id_utf8(IMMDevice* device, char* out, int out_len) {
    LPWSTR id_w = NULL;
    device->lpVtbl->GetId(device, &id_w);
    if (id_w) {
        WideCharToMultiByte(CP_UTF8, 0, id_w, -1, out, out_len, NULL, NULL);
        CoTaskMemFree(id_w);
    }
}

/* Detecte si un nom de device ressemble à un micro de camera */
static int is_camera_mic(const char* name) {
    /* Heuristique sur les noms courants de micros integres camera */
    const char* keywords[] = {
        "camera", "cam", "webcam", "logitech", "elgato",
        "obs", "capture", "facecam", NULL
    };
    char lower[256] = {0};
    for (int i = 0; name[i] && i < 255; i++)
        lower[i] = (char)tolower((unsigned char)name[i]);
    for (int k = 0; keywords[k]; k++)
        if (strstr(lower, keywords[k])) return 1;
    return 0;
}

/* Forward declare avant capture_list_all_audio car utilise dedans */
static BOOL CALLBACK _enum_windows_audio_cb(HWND hwnd, LPARAM lp) {
    typedef struct { AudioSourceInfo* out; int max; int count; } Ctx;
    Ctx* c = (Ctx*)lp;
    if (c->count >= c->max) return FALSE;
    if (!IsWindowVisible(hwnd)) return TRUE;
    char title[256] = {0};
    GetWindowTextA(hwnd, title, sizeof(title));
    if (strlen(title) == 0) return TRUE;
    if (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) return TRUE;

    AudioSourceInfo* s = &c->out[c->count];
    memset(s, 0, sizeof(*s));
    s->type = AUDIO_SOURCE_LOOPBACK_WINDOW;
    s->hwnd = hwnd;
    snprintf(s->label, sizeof(s->label), "[Loopback fenetre] %s", title);
    s->index = c->count++;
    return TRUE;
}

/* ------------------------------------------------------------------ *
 *  capture_list_all_audio
 *  Liste dans l'ordre :
 *    1. Loopback global (1 entree fixe)
 *    2. Fenêtres visibles (loopback par-fenêtre, best-effort)
 *    3. Microphones
 *    4. Micros camera (detectes par heuristique sur le nom)
 * ------------------------------------------------------------------ */
CASTOR_CORE_API int audio_capture_list_sources(AudioSourceInfo* out, int max_count) {
    int count = 0;

    /* 1. Loopback global */
    if (count < max_count) {
        AudioSourceInfo* s = &out[count++];
        memset(s, 0, sizeof(*s));
        s->type  = AUDIO_SOURCE_LOOPBACK_GLOBAL;
        s->hwnd  = NULL;
        s->index = count - 1;
        strncpy(s->label, "[Loopback] Systeme (global)", sizeof(s->label) - 1);
    }

    /* 2. Fenêtres visibles */
    {
        typedef struct { AudioSourceInfo* out; int max; int count; } WCtx;
        WCtx ctx2 = { out + count, max_count - count, 0 };
        EnumWindows(_enum_windows_audio_cb, (LPARAM)&ctx2);
        count += ctx2.count;
    }

    /* 3 & 4. Micros et micros camera via WASAPI eCapture */
    IMMDeviceEnumerator* enumerator = NULL;
    IMMDeviceCollection* collection = NULL;

    CoInitializeEx(NULL, COINIT_MULTITHREADED);
    HRESULT hr = CoCreateInstance(
        &local_CLSID_MMDeviceEnumerator, NULL, CLSCTX_ALL,
        &local_IID_IMMDeviceEnumerator, (void**)&enumerator);
    if (FAILED(hr)) return count;

    hr = enumerator->lpVtbl->EnumAudioEndpoints(
        enumerator, eCapture, DEVICE_STATE_ACTIVE, &collection);
    if (SUCCEEDED(hr)) {
        UINT device_count = 0;
        collection->lpVtbl->GetCount(collection, &device_count);

        for (UINT i = 0; i < device_count && count < max_count; i++) {
            IMMDevice* device = NULL;
            collection->lpVtbl->Item(collection, i, &device);

            IPropertyStore* props = NULL;
            device->lpVtbl->OpenPropertyStore(device, STGM_READ, &props);

            PROPVARIANT name_pv;
            PropVariantInit(&name_pv);
            props->lpVtbl->GetValue(props, &PKEY_Device_FriendlyName, &name_pv);

            char name_utf8[256] = {0};
            WideCharToMultiByte(CP_UTF8, 0, name_pv.pwszVal, -1,
                                name_utf8, sizeof(name_utf8), NULL, NULL);

            AudioSourceInfo* s = &out[count];
            memset(s, 0, sizeof(*s));
            s->index = count;

            /* Heuristique camera */
            if (is_camera_mic(name_utf8)) {
                s->type = AUDIO_SOURCE_CAMERA_MIC;
                snprintf(s->label, sizeof(s->label), "[Camera mic] %s", name_utf8);
            } else {
                s->type = AUDIO_SOURCE_MICROPHONE;
                snprintf(s->label, sizeof(s->label), "[Micro] %s", name_utf8);
            }

            get_device_id_utf8(device, s->device_id, sizeof(s->device_id));
            count++;

            PropVariantClear(&name_pv);
            props->lpVtbl->Release(props);
            device->lpVtbl->Release(device);
        }
        collection->lpVtbl->Release(collection);
    }

    enumerator->lpVtbl->Release(enumerator);
    return count;
}

/* ------------------------------------------------------------------ *
 *  audio_capture_init_from_device_id
 *  Init WASAPI sur un device de capture specifique (micro/camera mic)
 * ------------------------------------------------------------------ */
static int audio_capture_init_device_id(AudioCaptureContext* ctx, const char* device_id_utf8) {
    CoInitializeEx(NULL, COINIT_MULTITHREADED);

    AudioCaptureInternal* internal =
        (AudioCaptureInternal*)calloc(1, sizeof(AudioCaptureInternal));
    if (!internal) return -1;

    HRESULT hr;

    hr = CoCreateInstance(
        &local_CLSID_MMDeviceEnumerator, NULL, CLSCTX_ALL,
        &local_IID_IMMDeviceEnumerator, (void**)&internal->enumerator);
    if (FAILED(hr)) goto fail;

    /* Recuperer le device par son ID */
    WCHAR id_w[512] = {0};
    MultiByteToWideChar(CP_UTF8, 0, device_id_utf8, -1, id_w, 512);
    hr = internal->enumerator->lpVtbl->GetDevice(
        internal->enumerator, id_w, &internal->device);
    if (FAILED(hr)) {
        fprintf(stderr, "[AudioCapture] Device ID introuvable: %s\n", device_id_utf8);
        goto fail;
    }

    hr = internal->device->lpVtbl->Activate(
        internal->device, &local_IID_IAudioClient,
        CLSCTX_ALL, NULL, (void**)&internal->client);
    if (FAILED(hr)) goto fail;

    hr = internal->client->lpVtbl->GetMixFormat(
        internal->client, &internal->wave_fmt);
    if (FAILED(hr)) goto fail;

    /* Mode capture direct (pas loopback) */
    hr = internal->client->lpVtbl->Initialize(
        internal->client,
        AUDCLNT_SHAREMODE_SHARED,
        0,          /* pas de LOOPBACK_FLAG — c'est un vrai device de capture */
        10000000,
        0,
        internal->wave_fmt,
        NULL);
    if (FAILED(hr)) goto fail;

    hr = internal->client->lpVtbl->GetService(
        internal->client, &local_IID_IAudioCaptureClient,
        (void**)&internal->capture);
    if (FAILED(hr)) goto fail;

    internal->client->lpVtbl->Start(internal->client);

    internal->sample_rate = (int)internal->wave_fmt->nSamplesPerSec;
    internal->channels    = (int)internal->wave_fmt->nChannels;

    ctx->internal    = internal;
    ctx->sample_rate = internal->sample_rate;
    ctx->channels    = internal->channels;

    fprintf(stdout, "[AudioCapture] Device OK — %d Hz, %d ch\n",
            ctx->sample_rate, ctx->channels);
    return 0;

fail:
    fprintf(stderr, "[AudioCapture] Init device echouee (hr=0x%lx)\n", hr);
    if (internal->capture)    internal->capture->lpVtbl->Release(internal->capture);
    if (internal->client)     internal->client->lpVtbl->Release(internal->client);
    if (internal->device)     internal->device->lpVtbl->Release(internal->device);
    if (internal->enumerator) internal->enumerator->lpVtbl->Release(internal->enumerator);
    if (internal->wave_fmt)   CoTaskMemFree(internal->wave_fmt);
    free(internal);
    return -1;
}

/* ------------------------------------------------------------------ *
 *  audio_capture_init_file — ouvre un fichier via FFmpeg
 * ------------------------------------------------------------------ */
static int audio_capture_init_file(AudioCaptureContext* ctx, const char* path, int loop) {
    AudioFileCaptureInternal* internal =
        (AudioFileCaptureInternal*)calloc(1, sizeof(AudioFileCaptureInternal));
    if (!internal) return -1;
    internal->is_file = 1;
    internal->loop    = loop;

    int ret = avformat_open_input(&internal->fmt_ctx, path, NULL, NULL);
    if (ret < 0) {
        char err[128]; av_strerror(ret, err, sizeof(err));
        fprintf(stderr, "[FileAudio] Impossible d'ouvrir '%s': %s\n", path, err);
        free(internal);
        return -1;
    }

    if (avformat_find_stream_info(internal->fmt_ctx, NULL) < 0) {
        fprintf(stderr, "[FileAudio] find_stream_info echoue pour '%s'\n", path);
        avformat_close_input(&internal->fmt_ctx);
        free(internal);
        return -1;
    }

    internal->stream_idx = av_find_best_stream(
        internal->fmt_ctx, AVMEDIA_TYPE_AUDIO, -1, -1, NULL, 0);
    if (internal->stream_idx < 0) {
        fprintf(stderr, "[FileAudio] Aucun flux audio dans '%s'\n", path);
        avformat_close_input(&internal->fmt_ctx);
        free(internal);
        return -1;
    }

    AVStream*      stream = internal->fmt_ctx->streams[internal->stream_idx];
    const AVCodec* codec  = avcodec_find_decoder(stream->codecpar->codec_id);
    if (!codec) {
        fprintf(stderr, "[FileAudio] Codec audio introuvable\n");
        avformat_close_input(&internal->fmt_ctx);
        free(internal);
        return -1;
    }

    internal->codec_ctx = avcodec_alloc_context3(codec);
    avcodec_parameters_to_context(internal->codec_ctx, stream->codecpar);
    if (avcodec_open2(internal->codec_ctx, codec, NULL) < 0) {
        fprintf(stderr, "[FileAudio] avcodec_open2 echoue\n");
        avcodec_free_context(&internal->codec_ctx);
        avformat_close_input(&internal->fmt_ctx);
        free(internal);
        return -1;
    }

    internal->sample_rate = 48000;
    internal->channels    = 2;

    ctx->internal    = internal;
    ctx->sample_rate = internal->sample_rate;
    ctx->channels    = internal->channels;

    fprintf(stdout, "[FileAudio] Fichier ouvert : %s (loop=%d)\n", path, loop);
    return 0;
}

static AVFrame* next_frame_file_audio(AudioFileCaptureInternal* internal) {
    /* Initialise l'horloge de référence au premier appel.
     * On recule d'une durée de frame (~21333 µs) pour que le 1er frame
     * soit émis sans délai. Sans ça, le muxer RTMP reçoit les premiers
     * paquets vidéo 21 ms avant l'audio, doit les bufferiser, et Twitch
     * voit une désynchronisation + saccades au démarrage. */
    if (internal->start_us == 0) {
        int64_t one_frame_us = av_rescale(1024, AV_TIME_BASE, 48000); /* ~21333 µs */
        internal->start_us   = av_gettime_relative() - one_frame_us;
    }

    AVPacket* pkt = av_packet_alloc();
    if (!pkt) return NULL;

    for (;;) {
        int ret = av_read_frame(internal->fmt_ctx, pkt);
        if (ret < 0) {
            if (ret == AVERROR_EOF && internal->loop) {
                /* Sur boucle : vider le SWR pour éviter les samples résiduels,
                 * mais next_pts ne se réinitialise JAMAIS — garantit la monotonie. */
                if (internal->swr_ctx)
                    swr_convert(internal->swr_ctx, NULL, 0, NULL, 0);
                av_seek_frame(internal->fmt_ctx, -1, 0, AVSEEK_FLAG_BACKWARD);
                avcodec_flush_buffers(internal->codec_ctx);
                continue;
            }
            break;
        }
        if (pkt->stream_index != internal->stream_idx) {
            av_packet_unref(pkt);
            continue;
        }

        ret = avcodec_send_packet(internal->codec_ctx, pkt);
        av_packet_unref(pkt);
        if (ret < 0) break;

        AVFrame* decoded = av_frame_alloc();
        ret = avcodec_receive_frame(internal->codec_ctx, decoded);
        if (ret == AVERROR(EAGAIN)) { av_frame_free(&decoded); continue; }
        if (ret < 0)                { av_frame_free(&decoded); break;    }

        /* Initialise le resampler à la première frame (format source connu) */
        if (!internal->swr_ctx) {
            AVChannelLayout stereo;
            av_channel_layout_default(&stereo, 2);
            swr_alloc_set_opts2(&internal->swr_ctx,
                &stereo,          AV_SAMPLE_FMT_FLTP, 48000,
                &decoded->ch_layout, (enum AVSampleFormat)decoded->format, decoded->sample_rate,
                0, NULL);
            swr_init(internal->swr_ctx);
        }

        int out_samples = (int)av_rescale_rnd(
            swr_get_delay(internal->swr_ctx, decoded->sample_rate) + decoded->nb_samples,
            48000, decoded->sample_rate, AV_ROUND_UP);

        AVFrame* out = av_frame_alloc();
        out->format      = AV_SAMPLE_FMT_FLTP;
        out->sample_rate = 48000;
        out->nb_samples  = out_samples;
        av_channel_layout_default(&out->ch_layout, 2);

        if (av_frame_get_buffer(out, 0) < 0) {
            av_frame_free(&out);
            av_frame_free(&decoded);
            break;
        }

        int converted = swr_convert(internal->swr_ctx,
            out->data, out_samples,
            (const uint8_t**)decoded->data, decoded->nb_samples);
        out->nb_samples = converted;

        /* PTS en samples (monotone même après boucle) — jamais décroissant */
        out->pts = internal->next_pts;
        internal->next_pts += converted;

        av_frame_free(&decoded);

        /* Rate-limiting : on ne sort pas les frames plus vite que le temps réel.
         * Sans ça, le thread audio viderait le fichier en quelques ms et
         * inonderait l'encodeur, créant un décalage massif avec la vidéo. */
        int64_t expected_us = internal->start_us +
            av_rescale(internal->next_pts, AV_TIME_BASE, 48000);
        int64_t now = av_gettime_relative();
        if (expected_us > now + 1000)
            av_usleep((unsigned)(expected_us - now));

        av_packet_free(&pkt);
        return out;
    }

    av_packet_free(&pkt);
    return NULL;
}

/* ------------------------------------------------------------------ *
 *  audio_capture_init_source — dispatch selon AudioSourceType
 * ------------------------------------------------------------------ */
CASTOR_CORE_API int audio_capture_init_source(AudioCaptureContext* ctx, AudioSourceInfo* src) {
    switch (src->type) {
        case AUDIO_SOURCE_LOOPBACK_GLOBAL:
            return audio_capture_init(ctx, NULL);

        case AUDIO_SOURCE_LOOPBACK_WINDOW:
            if (src->hwnd) {
                int res = audio_capture_init_process_loopback(ctx, src->hwnd);
                if (res == 0) return 0;
                /* Fallback loopback global si le process loopback échoue (ex: Win < 19041) */
                fprintf(stderr, "[AudioCapture] Process loopback échoué → fallback global\n");
            }
            return audio_capture_init(ctx, NULL);

        case AUDIO_SOURCE_MICROPHONE:
        case AUDIO_SOURCE_CAMERA_MIC:
            return audio_capture_init_device_id(ctx, src->device_id);

        case AUDIO_SOURCE_FILE:
            return audio_capture_init_file(ctx, src->device_id, src->index);

        default:
            return -1;
    }
}

/* ------------------------------------------------------------------ *
 *  Dummy (conserve)
 * ------------------------------------------------------------------ */
CASTOR_CORE_API AVFrame* capture_dummy_audio_frame() {
    const int nb_samples  = 1024;
    const int sample_rate = 48000;

    AVFrame* frame = av_frame_alloc();
    if (!frame) return NULL;

    frame->nb_samples  = nb_samples;
    frame->format      = AV_SAMPLE_FMT_FLTP;
    frame->sample_rate = sample_rate;

    AVChannelLayout layout = {};
    av_channel_layout_default(&layout, 2);
    av_channel_layout_copy(&frame->ch_layout, &layout);
    av_channel_layout_uninit(&layout);

    if (av_frame_get_buffer(frame, 0) < 0) { av_frame_free(&frame); return NULL; }
    av_frame_make_writable(frame);

    float* left  = (float*)frame->data[0];
    float* right = (float*)frame->data[1];
    for (int i = 0; i < nb_samples; i++) {
        float sample = sinf(i * 0.01f);
        left[i] = right[i] = sample;
    }
    return frame;
}

/* ------------------------------------------------------------------ *
 *  CLI HELPER
 * ------------------------------------------------------------------ */
CASTOR_CORE_API int audio_capture_select_source_cli(AudioSourceInfo* out) {
    AudioSourceInfo sources[256];
    int count = audio_capture_list_sources(sources, 256);

    printf("\nSources audio disponibles :\n");
    for (int i = 0; i < count; i++)
        printf("  [%d] %s\n", i, sources[i].label);

    printf("Choisissez une source audio : ");
    int choice;
    scanf("%d", &choice);

    if (choice < 0 || choice >= count) {
        fprintf(stderr, "Choix invalide\n");
        return -1;
    }

    *out = sources[choice];
    return 0;
}

/* ================================================================== *
 *  SOURCE PLUGIN — "audio_capture"
 *  Integre directement dans AudioCapture.c pour eviter tout fichier
 *  intermediaire. Reutilise l'API C publique existante.
 * ================================================================== */

typedef struct {
    source_t*           source;
    AudioSourceInfo     src_info;
    AudioCaptureContext actx;
    pthread_t           thread;
    volatile int        running;
    int                 thread_started;
} AudioCaptureSrcData;

static void* ac_capture_thread(void* arg) {
    AudioCaptureSrcData* d = (AudioCaptureSrcData*)arg;
    while (d->running) {
        AVFrame* f = audio_capture_next_frame(&d->actx);
        if (f)
            source_output_frame(d->source, f);
    }
    return NULL;
}

static void* ac_create(source_t* self, void* settings) {
    AudioCaptureSrcData* d = (AudioCaptureSrcData*)calloc(1, sizeof(AudioCaptureSrcData));
    if (!d) return NULL;
    d->source = self;
    if (settings)
        d->src_info = *(AudioSourceInfo*)settings;
    return d;
}

static void ac_activate(void* data) {
    AudioCaptureSrcData* d = (AudioCaptureSrcData*)data;
    if (audio_capture_init_source(&d->actx, &d->src_info) < 0) {
        fprintf(stderr, "[audio_capture source] Echec init '%s'\n", d->src_info.label);
        return;
    }
    d->running = 1;
    if (pthread_create(&d->thread, NULL, ac_capture_thread, d) != 0) {
        d->running = 0;
        audio_capture_cleanup(&d->actx);
        fprintf(stderr, "[audio_capture source] Echec lancement thread '%s'\n", d->src_info.label);
        return;
    }
    d->thread_started = 1;
}

static void ac_deactivate(void* data) {
    AudioCaptureSrcData* d = (AudioCaptureSrcData*)data;
    if (!d) return;
    d->running = 0;
    if (d->thread_started) {
        pthread_join(d->thread, NULL);
        d->thread_started = 0;
    }
    if (d->actx.internal)
        audio_capture_cleanup(&d->actx);
}

static void ac_destroy(void* data) {
    free(data);
}

static source_info_t s_ac_info = {
    "audio_capture",
    ac_create,
    ac_destroy,
    ac_activate,
    ac_deactivate
};

CASTOR_CORE_API bool audio_capture_module_load(void) {
    source_register(&s_ac_info);
    return true;
}
