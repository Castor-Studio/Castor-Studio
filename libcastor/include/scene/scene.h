#pragma once

#include "castor_api.h"
#include "source/source.h"

typedef struct scene_item_t {
    source_t* source;

    int width;
    int height;
} scene_item_t;

CASTOR_CORE_API scene_item_t* scene_item_create(int width, int height);

CASTOR_CORE_API int scene_item_add_source(scene_item_t* item, source_t* source);

typedef struct scene_t {
    scene_item_t* items;
    int item_count;
} scene_t;

CASTOR_CORE_API scene_t* scene_create();

CASTOR_CORE_API int scene_add_item(scene_t* scene, scene_item_t* item);

CASTOR_CORE_API void scene_render(scene_t* scene);
