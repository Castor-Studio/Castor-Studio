#pragma once

#include "castor_api.h"
#include "source_info.h"

typedef DARRAY(source_info_t) source_info_array_t;

CASTOR_CORE_API void source_registry_init();

CASTOR_CORE_API void source_register(source_info_t* info);
CASTOR_CORE_API source_info_t* source_get_type(const char* id);

CASTOR_CORE_API void source_registry_print_all();