#include "PreviewRenderer.h"

extern "C" {
#include <libavutil/frame.h>
}

#include <d3d11.h>
#include <d3dcompiler.h>
#include <dxgi.h>
#include <process.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <windows.h>

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "d3dcompiler.lib")

struct CastorPreviewRenderer {
    HWND hwnd = nullptr;
    int target_width = 0;
    int target_height = 0;
    int fps = 30;

    CaptureSourceInfo source = {};
    VideoCaptureContext capture = {};

    ID3D11Device* device = nullptr;
    ID3D11DeviceContext* context = nullptr;
    IDXGISwapChain* swapchain = nullptr;
    ID3D11RenderTargetView* rtv = nullptr;
    ID3D11Texture2D* frame_texture = nullptr;
    ID3D11ShaderResourceView* frame_srv = nullptr;
    ID3D11SamplerState* sampler = nullptr;
    ID3D11VertexShader* vertex_shader = nullptr;
    ID3D11PixelShader* pixel_shader = nullptr;

    HANDLE thread = nullptr;
    volatile LONG running = 0;
    CRITICAL_SECTION lock;
    bool lock_initialized = false;
    bool capture_initialized = false;
    int texture_width = 0;
    int texture_height = 0;
};

static const char* PREVIEW_SHADER = R"(
Texture2D frameTex : register(t0);
SamplerState frameSampler : register(s0);

struct VsOut {
    float4 pos : SV_POSITION;
    float2 uv  : TEXCOORD0;
};

VsOut VSMain(uint id : SV_VertexID) {
    float2 pos[3] = {
        float2(-1.0, -1.0),
        float2(-1.0,  3.0),
        float2( 3.0, -1.0)
    };
    float2 uv[3] = {
        float2(0.0, 1.0),
        float2(0.0, -1.0),
        float2(2.0, 1.0)
    };
    VsOut output;
    output.pos = float4(pos[id], 0.0, 1.0);
    output.uv = uv[id];
    return output;
}

float4 PSMain(VsOut input) : SV_TARGET {
    return frameTex.Sample(frameSampler, input.uv);
}
)";

static void release_render_target(CastorPreviewRenderer* p) {
    if (p->rtv) {
        p->rtv->Release();
        p->rtv = nullptr;
    }
}

static void release_frame_texture(CastorPreviewRenderer* p) {
    if (p->frame_srv) {
        p->frame_srv->Release();
        p->frame_srv = nullptr;
    }
    if (p->frame_texture) {
        p->frame_texture->Release();
        p->frame_texture = nullptr;
    }
    p->texture_width = 0;
    p->texture_height = 0;
}

static void release_d3d(CastorPreviewRenderer* p) {
    release_frame_texture(p);
    release_render_target(p);
    if (p->pixel_shader) {
        p->pixel_shader->Release();
        p->pixel_shader = nullptr;
    }
    if (p->vertex_shader) {
        p->vertex_shader->Release();
        p->vertex_shader = nullptr;
    }
    if (p->sampler) {
        p->sampler->Release();
        p->sampler = nullptr;
    }
    if (p->swapchain) {
        p->swapchain->Release();
        p->swapchain = nullptr;
    }
    if (p->context) {
        p->context->Release();
        p->context = nullptr;
    }
    if (p->device) {
        p->device->Release();
        p->device = nullptr;
    }
}

static int update_target_size(CastorPreviewRenderer* p) {
    if (!p->hwnd) return -1;

    RECT rc = {};
    GetClientRect(p->hwnd, &rc);
    int width = rc.right - rc.left;
    int height = rc.bottom - rc.top;
    if (width <= 0 || height <= 0) {
        width = p->target_width > 0 ? p->target_width : 1;
        height = p->target_height > 0 ? p->target_height : 1;
    }

    p->target_width = width;
    p->target_height = height;
    return 0;
}

static int create_render_target(CastorPreviewRenderer* p) {
    release_render_target(p);

    ID3D11Texture2D* backbuffer = nullptr;
    HRESULT hr = p->swapchain->GetBuffer(0, IID_PPV_ARGS(&backbuffer));
    if (FAILED(hr) || !backbuffer) {
        fprintf(stderr, "[PreviewD3D] GetBuffer failed: 0x%lx\n", hr);
        return -1;
    }

    hr = p->device->CreateRenderTargetView(backbuffer, nullptr, &p->rtv);
    backbuffer->Release();
    if (FAILED(hr) || !p->rtv) {
        fprintf(stderr, "[PreviewD3D] CreateRenderTargetView failed: 0x%lx\n", hr);
        return -1;
    }
    return 0;
}

static int compile_shaders(CastorPreviewRenderer* p) {
    ID3DBlob* vs = nullptr;
    ID3DBlob* ps = nullptr;
    ID3DBlob* errors = nullptr;

    HRESULT hr = D3DCompile(PREVIEW_SHADER, strlen(PREVIEW_SHADER), nullptr, nullptr, nullptr,
                            "VSMain", "vs_4_0", 0, 0, &vs, &errors);
    if (FAILED(hr)) {
        if (errors) {
            fprintf(stderr, "[PreviewD3D] VS compile: %s\n", (char*)errors->GetBufferPointer());
            errors->Release();
        }
        return -1;
    }

    hr = D3DCompile(PREVIEW_SHADER, strlen(PREVIEW_SHADER), nullptr, nullptr, nullptr,
                    "PSMain", "ps_4_0", 0, 0, &ps, &errors);
    if (FAILED(hr)) {
        if (errors) {
            fprintf(stderr, "[PreviewD3D] PS compile: %s\n", (char*)errors->GetBufferPointer());
            errors->Release();
        }
        vs->Release();
        return -1;
    }

    hr = p->device->CreateVertexShader(vs->GetBufferPointer(), vs->GetBufferSize(), nullptr, &p->vertex_shader);
    vs->Release();
    if (FAILED(hr)) {
        ps->Release();
        return -1;
    }

    hr = p->device->CreatePixelShader(ps->GetBufferPointer(), ps->GetBufferSize(), nullptr, &p->pixel_shader);
    ps->Release();
    return FAILED(hr) ? -1 : 0;
}

static int ensure_d3d(CastorPreviewRenderer* p) {
    if (p->device && p->swapchain) return 0;
    if (!p->hwnd) return -1;
    update_target_size(p);

    DXGI_SWAP_CHAIN_DESC desc = {};
    desc.BufferCount = 2;
    desc.BufferDesc.Width = (UINT)p->target_width;
    desc.BufferDesc.Height = (UINT)p->target_height;
    desc.BufferDesc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.OutputWindow = p->hwnd;
    desc.SampleDesc.Count = 1;
    desc.Windowed = TRUE;
    desc.SwapEffect = DXGI_SWAP_EFFECT_DISCARD;

    UINT flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
    D3D_FEATURE_LEVEL levels[] = { D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0 };
    D3D_FEATURE_LEVEL selected = D3D_FEATURE_LEVEL_11_0;

    HRESULT hr = D3D11CreateDeviceAndSwapChain(
        nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, flags,
        levels, 2, D3D11_SDK_VERSION, &desc, &p->swapchain,
        &p->device, &selected, &p->context);

    if (FAILED(hr)) {
        hr = D3D11CreateDeviceAndSwapChain(
            nullptr, D3D_DRIVER_TYPE_WARP, nullptr, flags,
            levels, 2, D3D11_SDK_VERSION, &desc, &p->swapchain,
            &p->device, &selected, &p->context);
    }

    if (FAILED(hr) || !p->device || !p->context || !p->swapchain) {
        fprintf(stderr, "[PreviewD3D] D3D11CreateDeviceAndSwapChain failed: 0x%lx\n", hr);
        release_d3d(p);
        return -1;
    }

    if (create_render_target(p) < 0 || compile_shaders(p) < 0) {
        release_d3d(p);
        return -1;
    }

    D3D11_SAMPLER_DESC sampler_desc = {};
    sampler_desc.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
    sampler_desc.AddressU = D3D11_TEXTURE_ADDRESS_CLAMP;
    sampler_desc.AddressV = D3D11_TEXTURE_ADDRESS_CLAMP;
    sampler_desc.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
    sampler_desc.MaxLOD = D3D11_FLOAT32_MAX;
    hr = p->device->CreateSamplerState(&sampler_desc, &p->sampler);
    if (FAILED(hr)) {
        release_d3d(p);
        return -1;
    }

    return 0;
}

static int ensure_frame_texture(CastorPreviewRenderer* p, int width, int height) {
    if (p->frame_texture && p->texture_width == width && p->texture_height == height)
        return 0;

    release_frame_texture(p);

    D3D11_TEXTURE2D_DESC desc = {};
    desc.Width = (UINT)width;
    desc.Height = (UINT)height;
    desc.MipLevels = 1;
    desc.ArraySize = 1;
    desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    desc.SampleDesc.Count = 1;
    desc.Usage = D3D11_USAGE_DEFAULT;
    desc.BindFlags = D3D11_BIND_SHADER_RESOURCE;

    HRESULT hr = p->device->CreateTexture2D(&desc, nullptr, &p->frame_texture);
    if (FAILED(hr) || !p->frame_texture)
        return -1;

    D3D11_SHADER_RESOURCE_VIEW_DESC srv_desc = {};
    srv_desc.Format = desc.Format;
    srv_desc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
    srv_desc.Texture2D.MipLevels = 1;
    hr = p->device->CreateShaderResourceView(p->frame_texture, &srv_desc, &p->frame_srv);
    if (FAILED(hr) || !p->frame_srv) {
        release_frame_texture(p);
        return -1;
    }

    p->texture_width = width;
    p->texture_height = height;
    return 0;
}

static void render_frame(CastorPreviewRenderer* p, AVFrame* frame) {
    if (!frame || !frame->data[0] || frame->width <= 0 || frame->height <= 0)
        return;

    EnterCriticalSection(&p->lock);
    if (ensure_d3d(p) == 0 && ensure_frame_texture(p, frame->width, frame->height) == 0) {
        p->context->UpdateSubresource(
            p->frame_texture, 0, nullptr, frame->data[0],
            (UINT)frame->linesize[0], (UINT)(frame->linesize[0] * frame->height));

        FLOAT clear[4] = { 0.0f, 0.0f, 0.0f, 1.0f };
        p->context->OMSetRenderTargets(1, &p->rtv, nullptr);
        p->context->ClearRenderTargetView(p->rtv, clear);

        D3D11_VIEWPORT viewport = {};
        viewport.Width = (FLOAT)p->target_width;
        viewport.Height = (FLOAT)p->target_height;
        viewport.MinDepth = 0.0f;
        viewport.MaxDepth = 1.0f;
        p->context->RSSetViewports(1, &viewport);

        p->context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        p->context->VSSetShader(p->vertex_shader, nullptr, 0);
        p->context->PSSetShader(p->pixel_shader, nullptr, 0);
        p->context->PSSetShaderResources(0, 1, &p->frame_srv);
        p->context->PSSetSamplers(0, 1, &p->sampler);
        p->context->Draw(3, 0);

        ID3D11ShaderResourceView* null_srv = nullptr;
        p->context->PSSetShaderResources(0, 1, &null_srv);
        p->swapchain->Present(0, 0);
    }
    LeaveCriticalSection(&p->lock);
}

static unsigned __stdcall preview_thread(void* arg) {
    CastorPreviewRenderer* p = (CastorPreviewRenderer*)arg;
    const DWORD idle_sleep = p->fps > 0 ? (DWORD)(1000 / p->fps) : 16;

    while (InterlockedCompareExchange(&p->running, 1, 1) == 1) {
        AVFrame* frame = video_capture_next_frame(&p->capture);
        if (frame) {
            render_frame(p, frame);
            av_frame_free(&frame);
        } else {
            Sleep(idle_sleep);
        }
    }
    return 0;
}

extern "C" {

CASTOR_CORE_API CastorPreviewRenderer* preview_create(void) {
    CastorPreviewRenderer* p = new CastorPreviewRenderer();
    InitializeCriticalSection(&p->lock);
    p->lock_initialized = true;
    return p;
}

CASTOR_CORE_API int preview_attach_hwnd(CastorPreviewRenderer* p, void* hwnd) {
    if (!p || !hwnd) return -1;
    EnterCriticalSection(&p->lock);
    p->hwnd = (HWND)hwnd;
    int result = ensure_d3d(p);
    LeaveCriticalSection(&p->lock);
    return result;
}

CASTOR_CORE_API int preview_start(CastorPreviewRenderer* p, const CaptureSourceInfo* source, int fps) {
    if (!p || !source) return -1;
    if (InterlockedCompareExchange(&p->running, 1, 1) == 1) return 0;

    p->source = *source;
    p->fps = fps > 0 ? fps : 30;

    if (video_capture_init_source(&p->capture, &p->source) < 0)
        return -2;
    p->capture_initialized = true;

    InterlockedExchange(&p->running, 1);
    p->thread = (HANDLE)_beginthreadex(nullptr, 0, preview_thread, p, 0, nullptr);
    if (!p->thread) {
        InterlockedExchange(&p->running, 0);
        video_capture_cleanup(&p->capture);
        p->capture_initialized = false;
        return -3;
    }
    return 0;
}

CASTOR_CORE_API int preview_switch_source(CastorPreviewRenderer* p, const CaptureSourceInfo* source) {
    if (!p || !source) return -1;
    int was_running = InterlockedCompareExchange(&p->running, 1, 1) == 1;
    preview_stop(p);
    return was_running ? preview_start(p, source, p->fps) : 0;
}

CASTOR_CORE_API void preview_resize(CastorPreviewRenderer* p, int width, int height) {
    if (!p || width <= 0 || height <= 0) return;
    EnterCriticalSection(&p->lock);
    p->target_width = width;
    p->target_height = height;
    if (p->swapchain) {
        release_render_target(p);
        HRESULT hr = p->swapchain->ResizeBuffers(0, (UINT)width, (UINT)height, DXGI_FORMAT_UNKNOWN, 0);
        if (SUCCEEDED(hr)) {
            create_render_target(p);
        }
    }
    LeaveCriticalSection(&p->lock);
}

CASTOR_CORE_API void preview_stop(CastorPreviewRenderer* p) {
    if (!p) return;
    if (InterlockedExchange(&p->running, 0) == 1 && p->thread) {
        WaitForSingleObject(p->thread, 5000);
        CloseHandle(p->thread);
        p->thread = nullptr;
    }
    if (p->capture_initialized) {
        video_capture_cleanup(&p->capture);
        memset(&p->capture, 0, sizeof(p->capture));
        p->capture_initialized = false;
    }
}

CASTOR_CORE_API void preview_destroy(CastorPreviewRenderer* p) {
    if (!p) return;
    preview_stop(p);
    if (p->lock_initialized) {
        EnterCriticalSection(&p->lock);
        release_d3d(p);
        LeaveCriticalSection(&p->lock);
        DeleteCriticalSection(&p->lock);
        p->lock_initialized = false;
    }
    delete p;
}

}
