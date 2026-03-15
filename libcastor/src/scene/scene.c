#include <windows.h>

#include "scene.h"

scene_item_t* scene_item_create(int width, int height)
{
	scene_item_t* item = (scene_item_t*)malloc(sizeof(scene_item_t));
	if (!item)
		return NULL;

	item->source = NULL;
	item->width = width;
	item->height = height;

	return item;
}

int scene_item_add_source(scene_item_t* item, source_t* source)
{
	if (!item || !source)
		return -1;

	item->source = source;
	return 0;
}

scene_t* scene_create()
{
	scene_t* scene = (scene_t*)malloc(sizeof(scene_t));
	if (!scene)
		return NULL;

	scene->items = NULL;
	scene->item_count = 0;
	return scene;
}

int scene_add_item(scene_t* scene, scene_item_t* item)
{
	if (!scene || !item)
		return -1;

	size_t new_size = sizeof(scene_item_t) * (scene->item_count + 1);
	scene_item_t* new_items = (scene_item_t*)realloc(scene->items, new_size);
	if (!new_items)
		return -1;

	scene->items = new_items;
	scene->items[scene->item_count] = *item;
	scene->item_count++;

	return 0;
}

void scene_render(scene_t* scene)
{
	if (!scene)
		return;

	for (int i = 0; i < scene->item_count; i++)
	{
		scene_item_t* item = &scene->items[i];
		if (!item->source || !item->source->frame)
			continue;

		AVFrame* frame =
			InterlockedCompareExchangePointer(
				(PVOID*)&item->source->frame,
				NULL,
				NULL
			);

		if (frame)
		{
			AVFrame* local = av_frame_alloc();
			av_frame_ref(local, frame);

			printf("render: frame %dx%d pts=%lld\n",
				local->width,
				local->height,
				local->pts);

			av_frame_free(&local);
		}
	}
}