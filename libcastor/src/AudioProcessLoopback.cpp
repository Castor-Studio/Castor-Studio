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
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <synchapi.h>
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
 *  Contexte interne (meme layout qu'AudioCaptureInternal dans AudioCapture.c)
 * ------------------------------------------------------------------ */
typedef struct {
    IMMDeviceEnumerator*  enumerator;  /* NULL pour process loopback */
    IMMDevice*            device;      /* NULL pour process loopback */
    IAudioClient*         client;
    IAudioCaptureClient*  capture;
    WAVEFORMATEX*         wave_fmt;
    int                   sample_rate;
    int                   channels;
} AudioCaptureInternal;

/* ------------------------------------------------------------------ *
 *  audio_capture_init_process_loopback
 *  hwnd : fenêtre dont on veut capturer l'audio.
 *  Retourne 0 si succès, -1 si erreur (ou fallback si PID=0).
 * ------------------------------------------------------------------ */
extern "C"
int audio_capture_init_process_loopback(AudioCaptureContext* ctx, void* hwnd)
{
    if (!ctx || !hwnd) return -1;

    /* 1. Process ID depuis le HWND */
    DWORD pid = 0;
    GetWindowThreadProcessId(static_cast<HWND>(hwnd), &pid);
    if (pid == 0) {
        fprintf(stderr, "[ProcessLoopback] GetWindowThreadProcessId échoué\n");
        return -1;
    }

    CoInitializeEx(nullptr, COINIT_MULTITHREADED);

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

    /* 5. Format du mix */
    WAVEFORMATEX* wave_fmt = nullptr;
    hr = client->GetMixFormat(&wave_fmt);
    if (FAILED(hr)) {
        fprintf(stderr, "[ProcessLoopback] GetMixFormat échoué: 0x%lx\n", hr);
        client->Release();
        return -1;
    }

    /* 6. Initialize en mode loopback */
    hr = client->Initialize(
        AUDCLNT_SHAREMODE_SHARED,
        AUDCLNT_STREAMFLAGS_LOOPBACK,
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

    /* 7. IAudioCaptureClient */
    IAudioCaptureClient* capture = nullptr;
    hr = client->GetService(__uuidof(IAudioCaptureClient),
                            reinterpret_cast<void**>(&capture));
    if (FAILED(hr)) {
        fprintf(stderr, "[ProcessLoopback] GetService(IAudioCaptureClient) échoué: 0x%lx\n", hr);
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
        CoTaskMemFree(wave_fmt);
        client->Release();
        return -1;
    }

    internal->enumerator  = nullptr;
    internal->device      = nullptr;
    internal->client      = client;
    internal->capture     = capture;
    internal->wave_fmt    = wave_fmt;
    internal->sample_rate = static_cast<int>(wave_fmt->nSamplesPerSec);
    internal->channels    = static_cast<int>(wave_fmt->nChannels);

    ctx->internal    = internal;
    ctx->sample_rate = internal->sample_rate;
    ctx->channels    = internal->channels;

    fprintf(stdout, "[ProcessLoopback] OK — pid=%lu, %d Hz, %d ch\n",
            pid, ctx->sample_rate, ctx->channels);
    return 0;
}
