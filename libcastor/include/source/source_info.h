#pragma once

#include "castor_api.h"
#include "utils/darray.h"

struct source_t;

typedef struct source_info
{
    const char* id;

    /* self     : pointeur vers le source_t parent
     * settings : configuration specifique au type (ex: CaptureSourceInfo*), peut etre NULL */
    void* (*create)(struct source_t* self, void* settings);
    void (*destroy)(void* data);

    void (*activate)(void* data);
    void (*deactivate)(void* data);

} source_info_t;

CASTOR_CORE_API source_info_t* source_info_create(const char* id);