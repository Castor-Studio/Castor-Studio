#include <stdio.h>
#include <pthread.h>

#ifdef _WIN32
#include <windows.h>
#define sleep_ms(ms) Sleep(ms)
#else
#include <unistd.h>
#define sleep_ms(ms) usleep((ms) * 1000)
#endif

#include "source_registry.h"
#include "source.h"

typedef struct
{
    source_t* source;

	int running;
	pthread_t thread;
	
    int64_t frame_count;

} dummy_source_t;

static AVFrame* create_frame(int w, int h)
{
    AVFrame* f = av_frame_alloc();

    f->format = AV_PIX_FMT_RGB24;
    f->width = w;
    f->height = h;

    av_frame_get_buffer(f, 32);

    return f;
}

static void fill_dummy_frame(AVFrame* f, uint8_t value)
{
    for (int y = 0; y < f->height; y++)
    {
        uint8_t* row = f->data[0] + y * f->linesize[0];

        for (int x = 0; x < f->width * 3; x++)
            row[x] = value;
    }
}

static void* capture_thread(void* arg)
{
    dummy_source_t* src = arg;

    printf("Thread started\n");

    while (src->running)
    {
        AVFrame* frame = create_frame(640, 480);

        frame->pts = src->frame_count++;

        fill_dummy_frame(frame, 128);

        source_output_frame(src->source, frame);

		printf("[%p] Output frame %lld\n", src->thread.p, frame->pts);

        //sleep_ms(33);  // ~30 FPS
    }

    printf("Thread stopped\n");

    return NULL;
}

void* dummy_create(source_t* self, void* settings)
{
    (void)settings;

    dummy_source_t* dummy = malloc(sizeof(dummy_source_t));

    dummy->running = 0;
    dummy->source = self;
    dummy->frame_count = 0;

    return dummy;
}

void dummy_activate(void* data)
{
    dummy_source_t* dummy = data;

	dummy->running = 1;
	pthread_create(&dummy->thread, NULL, capture_thread, dummy);
}

void dummy_deactivate(void* data)
{
    dummy_source_t* dummy = data;

    dummy->running = 0;
	pthread_join(dummy->thread, NULL);
}

void dummy_destroy(void* data)
{
    dummy_source_t* dummy = data;
    free(dummy);
}

source_info_t dummy_source_info = {
    .id = "dummy_source",

    .create = dummy_create,
    .destroy = dummy_destroy,

    .activate = dummy_activate,
    .deactivate = dummy_deactivate
};

bool dummy_module_load(void)
{
    source_register(&dummy_source_info);

    return true;
}
