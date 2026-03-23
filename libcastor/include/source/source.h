#pragma once

#ifdef __cplusplus
extern "C" {
#endif

#include <libavutil/frame.h>
#include "castor_api.h"
#include "source_info.h"

typedef struct source_t {
	source_info_t* info;

	void* data;
	volatile AVFrame* frame;
} source_t;

/* id       : identifiant du type de source (doit etre enregistre dans le registre)
 * settings : donnees de configuration passees au callback create (ex: CaptureSourceInfo*) */
CASTOR_CORE_API source_t* source_create(const char* id, void* settings);
CASTOR_CORE_API void source_activate(source_t* src);
CASTOR_CORE_API void source_deactivate(source_t* src);
CASTOR_CORE_API void source_destroy(source_t* src);

CASTOR_CORE_API void source_output_frame(source_t* src, AVFrame* frame);
CASTOR_CORE_API AVFrame* source_get_frame(source_t* src);

bool dummy_module_load(void);

#ifdef __cplusplus
}
#endif