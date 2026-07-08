#pragma once

#include "castor_api.h"
#include "VideoCapture.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef struct CastorPreviewRenderer CastorPreviewRenderer;

CASTOR_CORE_API CastorPreviewRenderer* preview_create(void);
CASTOR_CORE_API int  preview_attach_hwnd(CastorPreviewRenderer* preview, void* hwnd);
CASTOR_CORE_API int  preview_start(CastorPreviewRenderer* preview, const CaptureSourceInfo* source, int fps);
CASTOR_CORE_API int  preview_switch_source(CastorPreviewRenderer* preview, const CaptureSourceInfo* source);
CASTOR_CORE_API void preview_resize(CastorPreviewRenderer* preview, int width, int height);
CASTOR_CORE_API void preview_stop(CastorPreviewRenderer* preview);
CASTOR_CORE_API void preview_destroy(CastorPreviewRenderer* preview);

#ifdef __cplusplus
}
#endif
