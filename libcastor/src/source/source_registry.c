#include "source_registry.h"

static source_info_array_t g_source_types = { 0 };

void source_registry_init()
{
	da_init(g_source_types);
}

void source_register(source_info_t* info)
{
	da_push_back(g_source_types, info);
}

source_info_t* source_get_type(const char* id)
{
    for (size_t i = 0; i < g_source_types.num; i++)
    {
        source_info_t* info = &g_source_types.array[i];

        if (strcmp(info->id, id) == 0)
            return info;
    }

    return NULL;
}

void source_registry_print_all()
{
    printf("Registered sources:\n");
    for (size_t i = 0; i < g_source_types.num; i++)
    {
        source_info_t* info = &g_source_types.array[i];
        printf(" - %s\n", info->id);
    }
}