#include "windows.h"
#include "source.h"

#include "source_registry.h"

source_t* source_create(const char* id)
{
    source_info_t* info = source_get_type(id);

    if (!info)
        return NULL;

    source_t* src = malloc(sizeof(source_t));

    src->info = info;
    src->data = info->create(src);
    src->frame = NULL;

    return src;
}

void source_activate(source_t* src)
{
    if (src->info->activate)
        src->info->activate(src->data);
}

void source_deactivate(source_t* src)
{
    if (src->info->deactivate)
        src->info->deactivate(src->data);
}

void source_destroy(source_t* src)
{
    if (src->info->destroy)
        src->info->destroy(src->data);

    if (src->frame)
        av_frame_free(&src->frame);

    free(src);
}

void source_output_frame(source_t* src, AVFrame* frame)
{
    AVFrame* old =
        InterlockedExchangePointer(
            (PVOID*)&src->frame,
            frame);

    if (old)
        av_frame_free(&old);
}

AVFrame* source_get_frame(source_t* src)
{
    return InterlockedCompareExchangePointer(
        (PVOID*)&src->frame,
        NULL,
        NULL);
}