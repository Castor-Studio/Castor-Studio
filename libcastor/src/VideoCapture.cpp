#include "VideoCapture.h"
#include "source/source.h"
#include "source/source_registry.h"

extern "C" {
    #include <libavutil/frame.h>
    #include <libavutil/imgutils.h>
    #include <libavutil/time.h>
}

#include <process.h>

// D3D/DXGI en premier
#include <d3d11_4.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <dwmapi.h>
#include <inspectable.h>

// IDirect3DDxgiInterfaceAccess — declare manuellement avant les interop headers
MIDL_INTERFACE("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")
IDirect3DDxgiInterfaceAccess : public IUnknown {
public:
    virtual HRESULT STDMETHODCALLTYPE GetInterface(REFIID iid, void** p) = 0;
};

// Interop headers APRÈS
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>

// WinRT
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>

// Media Foundation (webcam)
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <mferror.h>

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "dwmapi.lib")
#pragma comment(lib, "windowsapp.lib")
#pragma comment(lib, "mf.lib")
#pragma comment(lib, "mfplat.lib")
#pragma comment(lib, "mfreadwrite.lib")
#pragma comment(lib, "mfuuid.lib")

using namespace winrt;
using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX;
using namespace winrt::Windows::Graphics::DirectX::Direct3D11;

/* ------------------------------------------------------------------ *
 *  Contexte interne — deux chemins : WGC (ecran/fenêtre) et MF (cam)
 * ------------------------------------------------------------------ */
struct VideoCaptureContextInternal {
    bool is_camera = false;

    /* --- Chemin WGC --- */
    ID3D11Device*              d3d_device   = nullptr;
    ID3D11DeviceContext*       d3d_ctx      = nullptr;
    IDirect3DDevice            winrt_device = nullptr;
    GraphicsCaptureSession     session      = nullptr;
    Direct3D11CaptureFramePool frame_pool   = nullptr;
    HANDLE                     frame_event  = nullptr;

    /* --- Chemin MF (webcam) --- */
    IMFSourceReader* mf_reader = nullptr;
    int width  = 0;
    int height = 0;
};

extern "C" {

/* ================================================================== *
 *  LISTING
 * ================================================================== */

CASTOR_CORE_API int capture_list_windows(WindowInfo* out, int max_count) {
    struct Ctx { WindowInfo* out; int max; int count; };
    Ctx ctx = { out, max_count, 0 };

    EnumWindows([](HWND hwnd, LPARAM lParam) -> BOOL {
        auto* c = reinterpret_cast<Ctx*>(lParam);
        if (c->count >= c->max) return FALSE;
        if (!IsWindowVisible(hwnd)) return TRUE;
        char title[256] = {};
        GetWindowTextA(hwnd, title, sizeof(title));
        if (strlen(title) == 0) return TRUE;
        if (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) return TRUE;
        c->out[c->count].hwnd = hwnd;
        strncpy(c->out[c->count].title, title, 255);
        c->count++;
        return TRUE;
    }, reinterpret_cast<LPARAM>(&ctx));

    return ctx.count;
}

CASTOR_CORE_API int capture_list_monitors(MonitorInfo* out, int max_count) {
    struct Ctx { MonitorInfo* out; int max; int count; };
    Ctx ctx = { out, max_count, 0 };

    EnumDisplayMonitors(NULL, NULL, [](HMONITOR monitor, HDC, LPRECT, LPARAM lParam) -> BOOL {
        auto* c = reinterpret_cast<Ctx*>(lParam);
        if (c->count >= c->max) return FALSE;
        MONITORINFOEXA info = {};
        info.cbSize = sizeof(info);
        GetMonitorInfoA(monitor, &info);
        c->out[c->count].monitor = monitor;
        strncpy(c->out[c->count].name, info.szDevice, 31);
        c->out[c->count].rect = info.rcMonitor;
        c->count++;
        return TRUE;
    }, reinterpret_cast<LPARAM>(&ctx));

    return ctx.count;
}

CASTOR_CORE_API int video_capture_list_sources(CaptureSourceInfo* out, int max_count) {
    int count = 0;

    /* --- Moniteurs --- */
    struct MCtx { CaptureSourceInfo* out; int max; int count; };
    MCtx mctx = { out, max_count, 0 };

    EnumDisplayMonitors(NULL, NULL, [](HMONITOR monitor, HDC, LPRECT, LPARAM lp) -> BOOL {
        auto* c = reinterpret_cast<MCtx*>(lp);
        if (c->count >= c->max) return FALSE;
        MONITORINFOEXA info = {};
        info.cbSize = sizeof(info);
        GetMonitorInfoA(monitor, &info);
        auto& s    = c->out[c->count];
        s.type     = CAPTURE_SOURCE_MONITOR;
        s.hmonitor = monitor;
        s.hwnd     = nullptr;
        snprintf(s.label, sizeof(s.label), "[Ecran] %s", info.szDevice);
        s.index    = c->count++;
        return TRUE;
    }, reinterpret_cast<LPARAM>(&mctx));
    count += mctx.count;

    /* --- Fenêtres --- */
    struct WCtx { CaptureSourceInfo* out; int max; int count; };
    WCtx wctx = { out + count, max_count - count, 0 };

    EnumWindows([](HWND hwnd, LPARAM lp) -> BOOL {
        auto* c = reinterpret_cast<WCtx*>(lp);
        if (c->count >= c->max) return FALSE;
        if (!IsWindowVisible(hwnd)) return TRUE;
        char title[256] = {};
        GetWindowTextA(hwnd, title, sizeof(title));
        if (strlen(title) == 0) return TRUE;
        if (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) return TRUE;
        auto& s    = c->out[c->count];
        s.type     = CAPTURE_SOURCE_WINDOW;
        s.hwnd     = hwnd;
        s.hmonitor = nullptr;
        snprintf(s.label, sizeof(s.label), "[Fenetre] %s", title);
        s.index    = c->count++;
        return TRUE;
    }, reinterpret_cast<LPARAM>(&wctx));
    count += wctx.count;

    /* --- Cameras (MF) --- */
    MFStartup(MF_VERSION);
    IMFAttributes* attrs     = nullptr;
    IMFActivate**  devices   = nullptr;
    UINT32         cam_count = 0;

    MFCreateAttributes(&attrs, 1);
    attrs->SetGUID(MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE,
                   MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID);

    if (SUCCEEDED(MFEnumDeviceSources(attrs, &devices, &cam_count))) {
        for (UINT32 i = 0; i < cam_count && count < max_count; i++) {
            auto& s    = out[count];
            s.type     = CAPTURE_SOURCE_CAMERA;
            s.hwnd     = nullptr;
            s.hmonitor = nullptr;

            WCHAR name[256] = {};
            devices[i]->GetString(MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME,
                                  name, 256, nullptr);
            char name_utf8[256] = {};
            WideCharToMultiByte(CP_UTF8, 0, name, -1, name_utf8, 256, NULL, NULL);
            snprintf(s.label, sizeof(s.label), "[Camera] %s", name_utf8);

            WCHAR link[512] = {};
            devices[i]->GetString(
                MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK,
                link, 512, nullptr);
            WideCharToMultiByte(CP_UTF8, 0, link, -1, s.symbolic_link, 512, NULL, NULL);

            s.index = count++;
            devices[i]->Release();
        }
        CoTaskMemFree(devices);
    }
    if (attrs) attrs->Release();

    return count;
}

/* ================================================================== *
 *  INIT WGC (fenêtre / moniteur)
 * ================================================================== */

static int init_wgc_capture(VideoCaptureContext* ctx, GraphicsCaptureItem item) {
    // init_apartment est idempotent sur le même thread (renvoie S_FALSE si déjà MTA),
    // mais peut throw RPC_E_CHANGED_MODE si le thread est STA — on ignore cette erreur.
    try { winrt::init_apartment(winrt::apartment_type::multi_threaded); }
    catch (...) { /* déjà initialisé, c'est OK */ }

    auto* internal = new VideoCaptureContextInternal();
    internal->is_camera = false;

    HRESULT hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr,
        D3D11_CREATE_DEVICE_BGRA_SUPPORT, nullptr, 0,
        D3D11_SDK_VERSION, &internal->d3d_device, nullptr, &internal->d3d_ctx);

    if (FAILED(hr) || !internal->d3d_device) {
        fprintf(stderr, "[WGC] D3D11CreateDevice echoue: 0x%lx\n", hr);
        delete internal;
        return -1;
    }

    IDXGIDevice* dxgi_device = nullptr;
    hr = internal->d3d_device->QueryInterface(__uuidof(IDXGIDevice), (void**)&dxgi_device);
    if (FAILED(hr) || !dxgi_device) {
        fprintf(stderr, "[WGC] QueryInterface IDXGIDevice echoue: 0x%lx\n", hr);
        internal->d3d_ctx->Release();
        internal->d3d_device->Release();
        delete internal;
        return -1;
    }

    winrt::com_ptr<IInspectable> inspectable;
    hr = CreateDirect3D11DeviceFromDXGIDevice(dxgi_device, inspectable.put());
    dxgi_device->Release();
    if (FAILED(hr)) {
        fprintf(stderr, "[WGC] CreateDirect3D11DeviceFromDXGIDevice echoue: 0x%lx\n", hr);
        internal->d3d_ctx->Release();
        internal->d3d_device->Release();
        delete internal;
        return -1;
    }
    internal->winrt_device = inspectable.as<IDirect3DDevice>();

    auto size = item.Size();
    internal->width  = size.Width;
    internal->height = size.Height;

    internal->frame_pool = Direct3D11CaptureFramePool::CreateFreeThreaded(
        internal->winrt_device,
        DirectXPixelFormat::B8G8R8A8UIntNormalized,
        2, size);

    internal->frame_event = CreateEvent(nullptr, FALSE, FALSE, nullptr);

    internal->frame_pool.FrameArrived(
        [internal](Direct3D11CaptureFramePool const&,
                   winrt::Windows::Foundation::IInspectable const&) {
            SetEvent(internal->frame_event);
        });

    internal->session = internal->frame_pool.CreateCaptureSession(item);
    internal->session.StartCapture();

    ctx->internal = internal;
    ctx->width    = internal->width;
    ctx->height   = internal->height;
    return 0;
}

CASTOR_CORE_API int video_capture_init_window(VideoCaptureContext* ctx, void* hwnd) {
    try {
        auto interop = winrt::get_activation_factory<GraphicsCaptureItem, IGraphicsCaptureItemInterop>();
        GraphicsCaptureItem item = { nullptr };
        interop->CreateForWindow((HWND)hwnd, winrt::guid_of<GraphicsCaptureItem>(),
            reinterpret_cast<void**>(winrt::put_abi(item)));
        return init_wgc_capture(ctx, item);
    } catch (winrt::hresult_error const& e) {
        fprintf(stderr, "[WGC] init_window exception: 0x%lx\n", (long)e.code());
        return -1;
    } catch (...) {
        fprintf(stderr, "[WGC] init_window exception inconnue\n");
        return -1;
    }
}

CASTOR_CORE_API int video_capture_init_monitor(VideoCaptureContext* ctx, void* hmonitor) {
    try {
        auto interop = winrt::get_activation_factory<GraphicsCaptureItem, IGraphicsCaptureItemInterop>();
        GraphicsCaptureItem item = { nullptr };
        interop->CreateForMonitor((HMONITOR)hmonitor, winrt::guid_of<GraphicsCaptureItem>(),
            reinterpret_cast<void**>(winrt::put_abi(item)));
        return init_wgc_capture(ctx, item);
    } catch (winrt::hresult_error const& e) {
        fprintf(stderr, "[WGC] init_monitor exception: 0x%lx\n", (long)e.code());
        return -1;
    } catch (...) {
        fprintf(stderr, "[WGC] init_monitor exception inconnue\n");
        return -1;
    }
}

/* ================================================================== *
 *  INIT MF (webcam)
 * ================================================================== */

CASTOR_CORE_API int video_capture_init_camera(VideoCaptureContext* ctx, const char* symbolic_link) {
    MFStartup(MF_VERSION);

    auto* internal = new VideoCaptureContextInternal();
    internal->is_camera = true;

    WCHAR link_w[512] = {};
    MultiByteToWideChar(CP_UTF8, 0, symbolic_link, -1, link_w, 512);

    IMFAttributes* attrs = nullptr;
    MFCreateAttributes(&attrs, 2);
    attrs->SetGUID(MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE,
                   MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID);
    attrs->SetString(MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK, link_w);

    IMFMediaSource* source = nullptr;
    HRESULT hr = MFCreateDeviceSource(attrs, &source);
    attrs->Release();

    if (FAILED(hr)) {
        fprintf(stderr, "[Camera] MFCreateDeviceSource failed: 0x%lx\n", hr);
        delete internal;
        return -1;
    }

    /* SourceReader avec conversion auto de format activee */
    IMFAttributes* reader_attrs = nullptr;
    MFCreateAttributes(&reader_attrs, 1);
    reader_attrs->SetUINT32(MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, TRUE);

    hr = MFCreateSourceReaderFromMediaSource(source, reader_attrs, &internal->mf_reader);
    source->Release();
    reader_attrs->Release();

    if (FAILED(hr)) {
        fprintf(stderr, "[Camera] MFCreateSourceReaderFromMediaSource failed: 0x%lx\n", hr);
        delete internal;
        return -1;
    }

    /* Forcer sortie RGB32 (= BGRA côte FFmpeg) */
    IMFMediaType* out_type = nullptr;
    MFCreateMediaType(&out_type);
    out_type->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
    out_type->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_RGB32);
    hr = internal->mf_reader->SetCurrentMediaType(
        (DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM, nullptr, out_type);
    out_type->Release();

    if (FAILED(hr)) {
        fprintf(stderr, "[Camera] SetCurrentMediaType RGB32 failed: 0x%lx\n", hr);
        internal->mf_reader->Release();
        delete internal;
        return -1;
    }

    /* Recuperer les dimensions effectives */
    IMFMediaType* actual_type = nullptr;
    internal->mf_reader->GetCurrentMediaType(
        (DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM, &actual_type);
    if (actual_type) {
        UINT32 w = 0, h = 0;
        MFGetAttributeSize(actual_type, MF_MT_FRAME_SIZE, &w, &h);
        internal->width  = (int)w;
        internal->height = (int)h;
        actual_type->Release();
    }

    ctx->internal = internal;
    ctx->width    = internal->width;
    ctx->height   = internal->height;

    fprintf(stdout, "[Camera] Init OK — %dx%d\n", ctx->width, ctx->height);
    return 0;
}

/* ================================================================== *
 *  INIT SOURCE (dispatch selon type)
 * ================================================================== */

CASTOR_CORE_API int video_capture_init_source(VideoCaptureContext* ctx, CaptureSourceInfo* src) {
    switch (src->type) {
        case CAPTURE_SOURCE_WINDOW:  return video_capture_init_window(ctx, src->hwnd);
        case CAPTURE_SOURCE_MONITOR: return video_capture_init_monitor(ctx, src->hmonitor);
        case CAPTURE_SOURCE_CAMERA:  return video_capture_init_camera(ctx, src->symbolic_link);
        default: return -1;
    }
}

/* ================================================================== *
 *  NEXT FRAME
 * ================================================================== */

static AVFrame* next_frame_wgc(VideoCaptureContextInternal* internal) {
    if (WaitForSingleObject(internal->frame_event, 33)) {
        fprintf(stderr, "[WGC] Timeout\n");
        return nullptr;
    }

    Direct3D11CaptureFrame capture_frame = internal->frame_pool.TryGetNextFrame();
    if (!capture_frame) return nullptr;

    auto surface = capture_frame.Surface();

    IDirect3DDxgiInterfaceAccess* dxgi_access = nullptr;
    HRESULT hr = winrt::get_unknown(surface)->QueryInterface(
        __uuidof(IDirect3DDxgiInterfaceAccess), (void**)&dxgi_access);
    if (FAILED(hr) || !dxgi_access) {
        fprintf(stderr, "[WGC] QueryInterface failed: 0x%lx\n", hr);
        capture_frame.Close();
        return nullptr;
    }

    winrt::com_ptr<ID3D11Texture2D> texture;
    hr = dxgi_access->GetInterface(IID_PPV_ARGS(texture.put()));
    dxgi_access->Release();

    if (FAILED(hr) || !texture) {
        fprintf(stderr, "[WGC] GetInterface texture failed: 0x%lx\n", hr);
        capture_frame.Close();
        return nullptr;
    }

    D3D11_TEXTURE2D_DESC desc;
    texture->GetDesc(&desc);
    desc.Usage          = D3D11_USAGE_STAGING;
    desc.BindFlags      = 0;
    desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    desc.MiscFlags      = 0;

    ID3D11Texture2D* staging = nullptr;
    internal->d3d_device->CreateTexture2D(&desc, nullptr, &staging);
    internal->d3d_ctx->CopyResource(staging, texture.get());

    D3D11_MAPPED_SUBRESOURCE mapped;
    internal->d3d_ctx->Map(staging, 0, D3D11_MAP_READ, 0, &mapped);

    AVFrame* frame = av_frame_alloc();
    frame->format = AV_PIX_FMT_BGRA;
    frame->width  = (int)desc.Width;
    frame->height = (int)desc.Height;
    av_frame_get_buffer(frame, 0);

    for (int y = 0; y < (int)desc.Height; y++) {
        memcpy(frame->data[0] + y * frame->linesize[0],
               (uint8_t*)mapped.pData + y * mapped.RowPitch,
               desc.Width * 4);
    }

    internal->d3d_ctx->Unmap(staging, 0);
    staging->Release();
    capture_frame.Close();

    frame->pts = av_gettime_relative();
    return frame;
}

static AVFrame* next_frame_camera(VideoCaptureContextInternal* internal) {
    DWORD    stream_index = 0, flags = 0;
    LONGLONG timestamp    = 0;
    IMFSample* sample     = nullptr;

    HRESULT hr = internal->mf_reader->ReadSample(
        (DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM,
        0, &stream_index, &flags, &timestamp, &sample);

    if (FAILED(hr)) {
        fprintf(stderr, "[Camera] ReadSample failed: 0x%lx\n", hr);
        return nullptr;
    }
    if (!sample) {
        /* S_OK sans sample = camera pas encore prete ou frame droppee — on reessaie silencieusement. */
        return nullptr;
    }

    IMFMediaBuffer* buffer = nullptr;
    sample->ConvertToContiguousBuffer(&buffer);
    sample->Release();
    if (!buffer) return nullptr;

    BYTE* data    = nullptr;
    DWORD max_len = 0, cur_len = 0;
    buffer->Lock(&data, &max_len, &cur_len);

    AVFrame* frame = av_frame_alloc();
    frame->format = AV_PIX_FMT_BGRA;
    frame->width  = internal->width;
    frame->height = internal->height;
    av_frame_get_buffer(frame, 0);

    /*
     * MF livre RGB32 bottom-up (convention DIB).
     * On flip verticalement pour obtenir top-down.
     */
    const int bytes_per_row = internal->width * 4;
    for (int y = 0; y < internal->height; y++) {
        memcpy(frame->data[0] + y * frame->linesize[0],
           data + y * bytes_per_row,
           bytes_per_row);
    }

    buffer->Unlock();
    buffer->Release();

    frame->pts = timestamp / 10;
    return frame;
}

CASTOR_CORE_API AVFrame* video_capture_next_frame(VideoCaptureContext* ctx) {
    auto* internal = reinterpret_cast<VideoCaptureContextInternal*>(ctx->internal);
    if (!internal) return nullptr;
    return internal->is_camera ? next_frame_camera(internal)
                                : next_frame_wgc(internal);
}

/* ================================================================== *
 *  CLEANUP
 * ================================================================== */

CASTOR_CORE_API void video_capture_cleanup(VideoCaptureContext* ctx) {
    auto* internal = reinterpret_cast<VideoCaptureContextInternal*>(ctx->internal);
    if (!internal) return;

    if (internal->is_camera) {
        if (internal->mf_reader) internal->mf_reader->Release();
        MFShutdown();
    } else {
        if (internal->frame_event) CloseHandle(internal->frame_event);
        if (internal->session)     internal->session.Close();
        if (internal->frame_pool)  internal->frame_pool.Close();
        if (internal->d3d_ctx)     internal->d3d_ctx->Release();
        if (internal->d3d_device)  internal->d3d_device->Release();
    }

    delete internal;
    ctx->internal = nullptr;
}

/* ================================================================== *
 *  CLI HELPER
 * ================================================================== */

CASTOR_CORE_API int video_capture_select_source_cli(CaptureSourceInfo* out) {
    CaptureSourceInfo sources[256];
    int count = video_capture_list_sources(sources, 256);

    printf("Sources video disponibles :\n");
    for (int i = 0; i < count; i++)
        printf("  [%d] %s\n", i, sources[i].label);

    printf("Choisissez une source video : ");
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
 *  SOURCE PLUGIN — "video_capture"
 *  Integre directement dans VideoCapture.cpp pour eviter tout fichier
 *  intermediaire. Reutilise l'API C publique existante.
 * ================================================================== */

typedef struct {
    source_t*           source;
    CaptureSourceInfo   src_info;
    VideoCaptureContext vctx;
    HANDLE              thread;
    volatile int        running;
    int64_t             frame_count;
} VideoCaptureSrcData;

static unsigned __stdcall vc_capture_thread(void* arg) {
    VideoCaptureSrcData* d = (VideoCaptureSrcData*)arg;
    while (d->running) {
        AVFrame* f = video_capture_next_frame(&d->vctx);
        if (f) {
            f->pts = d->frame_count++;
            source_output_frame(d->source, f);
        }
    }
    return 0;
}

static void* vc_create(source_t* self, void* settings) {
    VideoCaptureSrcData* d = (VideoCaptureSrcData*)calloc(1, sizeof(VideoCaptureSrcData));
    if (!d) return NULL;
    d->source = self;
    if (settings)
        d->src_info = *(CaptureSourceInfo*)settings;
    return d;
}

static void vc_activate(void* data) {
    VideoCaptureSrcData* d = (VideoCaptureSrcData*)data;
    if (video_capture_init_source(&d->vctx, &d->src_info) < 0) {
        fprintf(stderr, "[video_capture source] Echec init '%s'\n", d->src_info.label);
        return;
    }
    d->running = 1;
    d->thread  = (HANDLE)_beginthreadex(NULL, 0, vc_capture_thread, d, 0, NULL);
}

static void vc_deactivate(void* data) {
    VideoCaptureSrcData* d = (VideoCaptureSrcData*)data;
    d->running = 0;
    if (d->thread) {
        WaitForSingleObject(d->thread, 5000);
        CloseHandle(d->thread);
        d->thread = NULL;
    }
    video_capture_cleanup(&d->vctx);
}

static void vc_destroy(void* data) {
    free(data);
}

static source_info_t s_vc_info = {
    "video_capture",
    vc_create,
    vc_destroy,
    vc_activate,
    vc_deactivate
};

CASTOR_CORE_API bool video_capture_module_load(void) {
    source_register(&s_vc_info);
    return true;
}

} // extern "C"