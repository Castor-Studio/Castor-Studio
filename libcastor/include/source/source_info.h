#pragma once

#include "castor_api.h"
#include "utils/darray.h"

typedef struct source_info
{
    const char* id;

    void* (*create)(void* data);
    void (*destroy)(void* data);

    void (*activate)(void* data);
    void (*deactivate)(void* data);

} source_info_t;

CASTOR_CORE_API source_info_t* source_info_create(const char* id);