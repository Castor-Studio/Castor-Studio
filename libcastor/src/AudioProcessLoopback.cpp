/*
 * AudioProcessLoopback.cpp
 *
 * Capture audio du process associe a un HWND via le Process Loopback API
 * de Windows 10 2004+ (build 19041).
 *
 * Expose une seule fonction C : audio_capture_init_process_loopback().
 */

#define INITGUID
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <objidl.h>       /* IAgileObject */
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <synchapi.h>
#include <process.h>
#include <stdio.h>
#include <string.h>

extern "C" {
#include "AudioCapture.h"
}

/* ------------------------------------------------------------------ *
 *  Structures Windows Process Loopback (SDK 10.0.20348+)
 * ------------------------------------------------------------------ */
typedef enum {
    AUDIOCLIENT_ACTIVATION_TYPE_DEFAULT          = 0,
    AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK = 1,
} AUDIOCLIENT_ACTIVATION_TYPE;

typedef enum {
    PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE = 0,
    PROCESS_LOOPBACK_MODE_EXCLUDE_TARGET_PROCESS_TREE = 1,
} PROCESS_LOOPBACK_MODE;

typedef struct {
    DWORD                TargetProcessId;
    PROCESS_LOOPBACK_MODE ProcessLoopbackMode;
} AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS;

typedef struct {
    AUDIOCLIENT_ACTIVATION_TYPE         ActivationType;
    AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS ProcessLoopbackParams;
} AUDIOCLIENT_ACTIVATION_PARAMS;

/* VAD\Process_Loopback : device virtuel utilise par le Process Loopback API */
static const wchar_t* VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK = L"VAD\\Process_Loopback";

/* ------------------------------------------------------------------ *
 *  IActivateAudioInterfaceCompletionHandler
 * ------------------------------------------------------------------ */
class ActivationHandler final : public IActivateAudioInterfaceCompletionHandler {
public:
    HANDLE       m_event;
    IAudioClient* m_client  = nullptr;
    HRESULT      m_result   = E_FAIL;
    ULONG        m_refs     = 1;

    ActivationHandler() {
        m_event = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    }
    ~ActivationHandler() {
        CloseHandle(m_event);
    }

    /* IUnknown */
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** ppv) override {
        if (riid == IID_IUnknown ||
            riid == __uuidof(IActivateAudioInterfaceCompletionHandler)) {
            *ppv = static_cast<IActivateAudioInterfaceCompletionHandler*>(this);
            AddRef();
            return S_OK;
        }
        if (riid == __uuidof(IAgileObject)) {
            /* Marqueur COM "agile" (free-threaded marshaling, cf. FtmBase
             * dans l'exemple ApplicationLoopback de Microsoft) : sans lui,
             * ActivateAudioInterfaceAsync rejette le handler avec
             * E_ILLEGAL_METHOD_CALL (0x8000000E) avant meme d'activer. */
            *ppv = static_cast<IUnknown*>(
                       static_cast<IActivateAudioInterfaceCompletionHandler*>(this));
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef()  override { return ++m_refs; }
    ULONG STDMETHODCALLTYPE Release() override {
        ULONG r = --m_refs;
        if (r == 0) delete this;
        return r;
    }

    /* IActivateAudioInterfaceCompletionHandler */
    HRESULT STDMETHODCALLTYPE ActivateCompleted(
        IActivateAudioInterfaceAsyncOperation* op) override
    {
        HRESULT hr_activate = S_OK;
        IUnknown* unk = nullptr;
        op->GetActivateResult(&hr_activate, &unk);
        m_result = hr_activate;
        if (SUCCEEDED(hr_activate) && unk) {
            unk->QueryInterface(__uuidof(IAudioClient),
                                reinterpret_cast<void**>(&m_client));
            unk->Release();
        }
        SetEvent(m_event);
        return S_OK;
    }

    bool Wait(DWORD timeoutMs = 5000) const {
        /* CoWaitForMultipleObjects pumpe les messages COM si le thread est STA
         * (cas courant depuis le runtime .NET), ce qui evite un deadlock ou
         * timeout lors de l'attente du callback ActivateCompleted. */
        DWORD idx = 0;
        HRESULT hr = CoWaitForMultipleObjects(COWAIT_DEFAULT, timeoutMs, 1, &m_event, &idx);
        return SUCCEEDED(hr) && idx == 0;
    }
};

/* ------------------------------------------------------------------ *
 *  Contexte interne — DOIT avoir exactement le meme layout
 *  qu'AudioCaptureInternal dans AudioCapture.c : audio_capture_next_frame
 *  et audio_capture_cleanup y accedent via cette definition-la.
 *  En particulier is_file doit rester le premier membre (dispatch generique).
 * ------------------------------------------------------------------ */
typedef struct {
    int                   is_file;     /* 0 = WASAPI */
    IMMDeviceEnumerator*  enumerator;  /* NULL pour process loopback */
    IMMDevice*            device;      /* NULL pour process loopback */
    IAudioClient*         client;
    IAudioCaptureClient*  capture;
    WAVEFORMATEX*         wave_fmt;
    int                   sample_rate;
    int                   channels;
    HANDLE                event;       /* event du mode EVENTCALLBACK */
} AudioCaptureInternal;

/* ------------------------------------------------------------------ *
 *  Implementation — DOIT s'executer sur un thread MTA :
 *  ActivateAudioInterfaceAsync retourne E_ILLEGAL_METHOD_CALL
 *  (0x8000000E) depuis un thread STA (cas du thread UI .NET/Avalonia).
 * ------------------------------------------------------------------ */
static int init_process_loopback_impl(AudioCaptureContext* ctx, HWND hwnd)
{
    /* 1. Process ID depuis le HWND */
    DWORD pid = 0;
    GetWindowThreadProcessId(hwnd, &pid);
    if (pid == 0) {
        fprintf(stderr, "[ProcessLoopback] GetWindowThreadProcessId échoué\n");
        return -1;
    }

    /* 2. AUDIOCLIENT_ACTIVATION_PARAMS → PROPVARIANT */
    AUDIOCLIENT_ACTIVATION_PARAMS params = {};
    params.ActivationType                               = AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK;
    params.ProcessLoopbackParams.TargetProcessId        = pid;
    params.ProcessLoopbackParams.ProcessLoopbackMode    = PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE;

    PROPVARIANT pv = {};
    pv.vt            = VT_BLOB;
    pv.blob.cbSize   = sizeof(params);
    pv.blob.pBlobData = reinterpret_cast<BYTE*>(&params);

    /* 3. ActivateAudioInterfaceAsync */
    auto* handler = new ActivationHandler();
    IActivateAudioInterfaceAsyncOperation* asyncOp = nullptr;

    HRESULT hr = ActivateAudioInterfaceAsync(
        VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK,
        __uuidof(IAudioClient),
        &pv,
        handler,
        &asyncOp);

    if (FAILED(hr)) {
        fprintf(stderr, "[ProcessLoopback] ActivateAudioInterfaceAsync échoué: 0x%lx\n", hr);
        handler->Release();
        if (asyncOp) asyncOp->Release();
        return -1;
    }

    /* 4. Attendre la complétion (max 5s) */
    if (!handler->Wait()) {
        fprintf(stderr, "[ProcessLoopback] Timeout activation (pid=%lu)\n", pid);
        handler->Release();
        if (asyncOp) asyncOp->Release();
        return -1;
    }

    if (FAILED(handler->m_result) || !handler->m_client) {
        fprintf(stderr, "[ProcessLoopback] Activation échouée: 0x%lx (pid=%lu)\n",
                handler->m_result, pid);
        handler->Release();
        if (asyncOp) asyncOp->Release();
        return -1;
    }

    IAudioClient* client = handler->m_client;
    handler->m_client = nullptr;
    handler->Release();
    if (asyncOp) asyncOp->Release();

    /* 5. Format de capture.
     * Le client process-loopback (VAD\Process_Loopback) est un device
     * virtuel : il ne supporte PAS GetMixFormat, le format doit etre
     * fourni par l'appelant. Float 32 stereo 48 kHz — format gere
     * nativement par wasapi_to_avframe (WAVE_FORMAT_IEEE_FLOAT). */
    WAVEFORMATEX* wave_fmt = (WAVEFORMATEX*)CoTaskMemAlloc(sizeof(WAVEFORMATEX));
    if (!wave_fmt) {
        client->Release();
        return -1;
    }
    memset(wave_fmt, 0, sizeof(*wave_fmt));
    wave_fmt->wFormatTag      = WAVE_FORMAT_IEEE_FLOAT;
    wave_fmt->nChannels       = 2;
    wave_fmt->nSamplesPerSec  = 48000;
    wave_fmt->wBitsPerSample  = 32;
    wave_fmt->nBlockAlign     = (WORD)(wave_fmt->nChannels * wave_fmt->wBitsPerSample / 8);
    wave_fmt->nAvgBytesPerSec = wave_fmt->nSamplesPerSec * wave_fmt->nBlockAlign;

    /* 6. Initialize en mode loopback + event callback.
     * Le mode event-driven est requis par le process loopback ; on fournit
     * l'event mais la consommation reste par polling (GetNextPacketSize),
     * identique aux autres chemins WASAPI. */
    hr = client->Initialize(
        AUDCLNT_SHAREMODE_SHARED,
        AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_EVENTCALLBACK,
        10000000,   /* buffer 1s en unités 100ns */
        0,
        wave_fmt,
        nullptr);
    if (FAILED(hr)) {
        fprintf(stderr, "[ProcessLoopback] Initialize échoué: 0x%lx\n", hr);
        CoTaskMemFree(wave_fmt);
        client->Release();
        return -1;
    }

    HANDLE event = CreateEventW(nullptr, FALSE, FALSE, nullptr);
    if (!event || FAILED(client->SetEventHandle(event))) {
        fprintf(stderr, "[ProcessLoopback] SetEventHandle échoué\n");
        if (event) CloseHandle(event);
        CoTaskMemFree(wave_fmt);
        client->Release();
        return -1;
    }

    /* 7. IAudioCaptureClient */
    IAudioCaptureClient* capture = nullptr;
    hr = client->GetService(__uuidof(IAudioCaptureClient),
                            reinterpret_cast<void**>(&capture));
    if (FAILED(hr)) {
        fprintf(stderr, "[ProcessLoopback] GetService(IAudioCaptureClient) échoué: 0x%lx\n", hr);
        CloseHandle(event);
        CoTaskMemFree(wave_fmt);
        client->Release();
        return -1;
    }

    client->Start();

    /* 8. Remplir le contexte (même layout qu'AudioCaptureInternal) */
    AudioCaptureInternal* internal =
        static_cast<AudioCaptureInternal*>(calloc(1, sizeof(AudioCaptureInternal)));
    if (!internal) {
        capture->Release();
        CloseHandle(event);
        CoTaskMemFree(wave_fmt);
        client->Release();
        return -1;
    }

    internal->is_file     = 0;
    internal->enumerator  = nullptr;
    internal->device      = nullptr;
    internal->client      = client;
    internal->capture     = capture;
    internal->wave_fmt    = wave_fmt;
    internal->sample_rate = static_cast<int>(wave_fmt->nSamplesPerSec);
    internal->channels    = static_cast<int>(wave_fmt->nChannels);
    internal->event       = event;

    ctx->internal    = internal;
    ctx->sample_rate = internal->sample_rate;
    ctx->channels    = internal->channels;

    fprintf(stderr, "[ProcessLoopback] OK — pid=%lu, %d Hz, %d ch\n",
            pid, ctx->sample_rate, ctx->channels);
    return 0;
}

/* ------------------------------------------------------------------ *
 *  audio_capture_init_process_loopback
 *  hwnd : fenêtre dont on veut capturer l'audio.
 *  Retourne 0 si succès, -1 si erreur.
 *
 *  ActivateAudioInterfaceAsync exige un thread MTA. Si l'appelant est
 *  deja MTA (threads natifs du recorder), on execute l'init en place ;
 *  si l'appelant est STA (thread UI .NET/Avalonia via recorder_start),
 *  on delegue a un thread dedie — sinon echec 0x8000000E systematique
 *  et fallback silencieux sur le loopback global.
 * ------------------------------------------------------------------ */
typedef struct {
    AudioCaptureContext* ctx;
    HWND                 hwnd;
    int                  result;
} LoopbackInitArgs;

static unsigned __stdcall init_thread_proc(void* arg)
{
    LoopbackInitArgs* a = static_cast<LoopbackInitArgs*>(arg);
    /* Thread neuf → l'init MTA reussit toujours. Pas de CoUninitialize :
     * les interfaces WASAPI creees ici doivent survivre a ce thread, on
     * laisse donc l'appartement MTA du process actif. */
    CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    a->result = init_process_loopback_impl(a->ctx, a->hwnd);
    return 0;
}

extern "C"
int audio_capture_init_process_loopback(AudioCaptureContext* ctx, void* hwnd)
{
    if (!ctx || !hwnd) return -1;

    HRESULT hr_co = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (hr_co != RPC_E_CHANGED_MODE) {
        /* Le thread courant est (desormais) MTA. */
        return init_process_loopback_impl(ctx, static_cast<HWND>(hwnd));
    }

    /* Thread STA : deleguer a un thread MTA dedie. */
    LoopbackInitArgs args = { ctx, static_cast<HWND>(hwnd), -1 };
    HANDLE th = (HANDLE)_beginthreadex(nullptr, 0, init_thread_proc, &args, 0, nullptr);
    if (!th) {
        fprintf(stderr, "[ProcessLoopback] Creation du thread MTA échouée\n");
        return -1;
    }
    /* INFINITE : args est sur la pile — l'impl a ses propres timeouts (5s). */
    WaitForSingleObject(th, INFINITE);
    CloseHandle(th);
    return args.result;
}
