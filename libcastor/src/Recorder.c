#include "Recorder.h"
#include "VideoEncode.h"
#include "AudioEncode.h"
#include "Muxer.h"

#include <windows.h>
#include <process.h>
#include <stdlib.h>
#include <stdio.h>
#include <libavutil/frame.h>
#include <libswscale/swscale.h>

/* ------------------------------------------------------------------ *
 *  Etat interne d'un stream (opaque hors de ce fichier)
 * ------------------------------------------------------------------ */
typedef struct CastorRecorder CastorRecorder;

typedef struct {
    StreamConfig        config;

    VideoCaptureContext vctx;
    AudioCaptureContext actx;
    VideoEncoder        venc;
    AudioEncoder        aenc;
    CastorMuxer         mux;

    AVFrame*            last_video_frame;
    CRITICAL_SECTION    frame_lock;

    HANDLE              th_video_capture;
    HANDLE              th_video_encode;
    HANDLE              th_audio;

    volatile int        capture_running;   /* controle uniquement le thread capture    */
    volatile int        first_frame_ready; /* mis a 1 apres la 1ere frame encodee      */
    int                 initialized;       /* 1 si stream_init a reussi                */
    CastorRecorder*     recorder;        /* reference vers le recorder parent      */
    int                 index;           /* index dans le tableau streams[]        */
} StreamState;

/* ------------------------------------------------------------------ *
 *  Structure principale (opaque dans le header)
 * ------------------------------------------------------------------ */
struct CastorRecorder {
    RecorderConfig  config;
    StreamState     streams[CASTOR_MAX_STREAMS];
    int             num_streams;
    volatile int    running;
};

/* ================================================================== *
 *  Threads par stream
 * ================================================================== */

/* Thread 1 : capture video — alimente last_video_frame en continu */
static unsigned __stdcall thread_stream_video_capture(void* arg) {
    StreamState* s = (StreamState*)arg;
    while (s->recorder->running && s->capture_running) {
        AVFrame* f = video_capture_next_frame(&s->vctx);
        if (f) {
            EnterCriticalSection(&s->frame_lock);
            if (s->last_video_frame) av_frame_free(&s->last_video_frame);
            s->last_video_frame = f;
            LeaveCriticalSection(&s->frame_lock);
        }
    }
    return 0;
}

/* Thread 2 : encodage video — boucle fps strict */
static unsigned __stdcall thread_stream_video_encode(void* arg) {
    StreamState* s = (StreamState*)arg;

    timeBeginPeriod(1);

    LARGE_INTEGER freq, start, now;
    QueryPerformanceFrequency(&freq);
    QueryPerformanceCounter(&start);

    const double frame_interval = 1.0 / s->recorder->config.fps;
    double       next_frame_time = 0.0;
    int          frames_encoded  = 0;

    while (s->recorder->running) {
        QueryPerformanceCounter(&now);
        double elapsed = (double)(now.QuadPart - start.QuadPart) / freq.QuadPart;

        if (elapsed < next_frame_time) {
            Sleep(1);
            continue;
        }
        next_frame_time += frame_interval;

        EnterCriticalSection(&s->frame_lock);
        AVFrame* vframe = s->last_video_frame
                          ? av_frame_clone(s->last_video_frame)
                          : NULL;
        LeaveCriticalSection(&s->frame_lock);

        if (vframe) {
            video_encoder_encode_frame(&s->venc, vframe, &s->mux);
            av_frame_free(&vframe);
            frames_encoded++;
            if (frames_encoded == 1)
                s->first_frame_ready = 1;
        }
    }

    timeEndPeriod(1);

    QueryPerformanceCounter(&now);
    double total = (double)(now.QuadPart - start.QuadPart) / freq.QuadPart;
    if (total > 0.0)
        fprintf(stderr, "[Stream %d] Total: %.2fs — %d frames — fps effectif: %.1f\n",
                s->index, total, frames_encoded, frames_encoded / total);

    return 0;
}

/* Thread 3 : capture + encodage audio
 *
 * Strategie de synchronisation basee sur l'horloge QPC absolue :
 * On compare en permanence le nombre de samples soumis au FIFO
 * (submitted) avec le nombre attendu selon le temps ecoule depuis t0.
 * Tout ecart est comble par du silence. */
static unsigned __stdcall thread_stream_audio(void* arg) {
    StreamState* s = (StreamState*)arg;

    const int sample_rate = s->actx.sample_rate > 0 ? s->actx.sample_rate : 48000;
    const int channels    = s->actx.channels;

    /* Vider le buffer WASAPI pendant le warm-up de la source video.
     * Demarre l'horloge QPC uniquement quand la 1ere frame video est encodee,
     * ce qui garantit la synchronisation A/V meme si la source (camera, MF)
     * met du temps a livrer son premier frame. */
    while (s->recorder->running && !s->first_frame_ready) {
        AVFrame* drain = audio_capture_next_frame(&s->actx);
        if (drain) av_frame_free(&drain);
    }
    if (!s->recorder->running) return 0;

    LARGE_INTEGER freq, t0;
    QueryPerformanceFrequency(&freq);
    QueryPerformanceCounter(&t0);

    int64_t submitted = 0;

    while (s->recorder->running) {
        AVFrame* aframe = audio_capture_next_frame(&s->actx);

        LARGE_INTEGER t_now;
        QueryPerformanceCounter(&t_now);
        double  elapsed  = (double)(t_now.QuadPart - t0.QuadPart) / freq.QuadPart;
        int64_t expected = (int64_t)(elapsed * sample_rate);

        int     wasapi_samples = aframe ? aframe->nb_samples : 0;
        int64_t gap = expected - submitted - wasapi_samples;
        if (gap < 0) gap = 0;

        if (gap > 0) {
            AVFrame* silence = av_frame_alloc();
            silence->format      = AV_SAMPLE_FMT_FLTP;
            silence->sample_rate = sample_rate;
            silence->nb_samples  = (int)gap;
            av_channel_layout_default(&silence->ch_layout, channels);
            if (av_frame_get_buffer(silence, 0) == 0) {
                for (int ch = 0; ch < silence->ch_layout.nb_channels; ch++)
                    memset(silence->data[ch], 0, silence->nb_samples * sizeof(float));
                audio_encoder_encode_frame(&s->aenc, silence, &s->mux);
                submitted += gap;
            }
            av_frame_free(&silence);
        }

        if (aframe) {
            audio_encoder_encode_frame(&s->aenc, aframe, &s->mux);
            submitted += wasapi_samples;
            av_frame_free(&aframe);
        }
    }
    return 0;
}

/* ================================================================== *
 *  Init / cleanup d'un stream
 * ================================================================== */

static int stream_init(StreamState* s) {
    if (video_capture_init_source(&s->vctx, &s->config.video_src) < 0) {
        fprintf(stderr, "[Stream %d] Init capture video echouee\n", s->index);
        return -1;
    }

    if (audio_capture_init_source(&s->actx, &s->config.audio_src) < 0) {
        fprintf(stderr, "[Stream %d] Init capture audio echouee\n", s->index);
        video_capture_cleanup(&s->vctx);
        return -1;
    }
    if (s->actx.channels > 2) s->actx.channels = 2;

    if (video_encoder_init(&s->venc, s->vctx.width, s->vctx.height, s->recorder->config.fps) < 0) {
        fprintf(stderr, "[Stream %d] Init encodeur video echoue\n", s->index);
        video_capture_cleanup(&s->vctx);
        audio_capture_cleanup(&s->actx);
        return -1;
    }

    if (audio_encoder_init(&s->aenc, s->actx.sample_rate) < 0) {
        fprintf(stderr, "[Stream %d] Init encodeur audio echoue\n", s->index);
        video_encoder_cleanup(&s->venc, &s->mux);
        video_capture_cleanup(&s->vctx);
        audio_capture_cleanup(&s->actx);
        return -1;
    }

    if (muxer_open(&s->mux, s->config.output_path) < 0          ||
        muxer_add_video_stream(&s->mux, s->venc.ctx) < 0        ||
        muxer_add_audio_stream(&s->mux, s->aenc.ctx) < 0        ||
        muxer_write_header(&s->mux) < 0) {
        fprintf(stderr, "[Stream %d] Init muxer echouee\n", s->index);
        muxer_close(&s->mux);
        video_encoder_cleanup(&s->venc, &s->mux);
        audio_encoder_cleanup(&s->aenc, &s->mux);
        video_capture_cleanup(&s->vctx);
        audio_capture_cleanup(&s->actx);
        return -1;
    }

    InitializeCriticalSection(&s->frame_lock);
    s->initialized = 1;
    return 0;
}

static void stream_start_threads(StreamState* s) {
    s->capture_running  = 1;
    s->th_video_capture = (HANDLE)_beginthreadex(NULL, 0, thread_stream_video_capture, s, 0, NULL);
    s->th_video_encode  = (HANDLE)_beginthreadex(NULL, 0, thread_stream_video_encode,  s, 0, NULL);
    s->th_audio         = (HANDLE)_beginthreadex(NULL, 0, thread_stream_audio,          s, 0, NULL);
}

static void stream_stop_threads(StreamState* s) {
    s->capture_running = 0;
    HANDLE threads[3] = { s->th_video_capture, s->th_video_encode, s->th_audio };
    for (int t = 0; t < 3; t++)
        if (threads[t]) WaitForSingleObject(threads[t], 5000);
    for (int t = 0; t < 3; t++)
        if (threads[t]) CloseHandle(threads[t]);
    s->th_video_capture = NULL;
    s->th_video_encode  = NULL;
    s->th_audio         = NULL;
}

static void stream_cleanup(StreamState* s) {
    if (!s->initialized) return;

    DeleteCriticalSection(&s->frame_lock);
    if (s->last_video_frame) {
        av_frame_free(&s->last_video_frame);
        s->last_video_frame = NULL;
    }

    video_encoder_cleanup(&s->venc, &s->mux);
    audio_encoder_cleanup(&s->aenc, &s->mux);
    muxer_close(&s->mux);
    video_capture_cleanup(&s->vctx);
    audio_capture_cleanup(&s->actx);

    s->initialized = 0;
}

/* ================================================================== *
 *  Transition de source video — isole pour extensions futures
 *
 *  Point d'extension : implementer ici les effets de transition
 *  (cut, fondu, crossfade) en remplacant ou enveloppant cette fonction.
 * ================================================================== */
static int stream_apply_source_switch(StreamState* s, const CaptureSourceInfo* new_src) {
    /* 1. Arreter uniquement le thread de capture */
    s->capture_running = 0;
    if (s->th_video_capture) {
        WaitForSingleObject(s->th_video_capture, 5000);
        CloseHandle(s->th_video_capture);
        s->th_video_capture = NULL;
    }

    /* 2. Liberer l'ancienne source */
    video_capture_cleanup(&s->vctx);
    memset(&s->vctx, 0, sizeof(s->vctx));

    /* 3. Initialiser la nouvelle source */
    s->config.video_src = *new_src;
    if (video_capture_init_source(&s->vctx, &s->config.video_src) < 0) {
        fprintf(stderr, "[Stream %d] Switch: init nouvelle source echouee\n", s->index);
        return -1;
    }

    /* 4. Recrer le contexte de conversion si les dimensions changent
     *    (la resolution du fichier de sortie reste inchangee, la nouvelle
     *    source est mise a l'echelle pour correspondre) */
    if (s->vctx.width != s->venc.ctx->width || s->vctx.height != s->venc.ctx->height) {
        sws_freeContext(s->venc.sws_ctx);
        s->venc.sws_ctx = sws_getContext(
            s->vctx.width,      s->vctx.height,      AV_PIX_FMT_BGRA,
            s->venc.ctx->width, s->venc.ctx->height,  AV_PIX_FMT_YUV420P,
            SWS_BILINEAR, NULL, NULL, NULL
        );
        if (!s->venc.sws_ctx) {
            fprintf(stderr, "[Stream %d] Switch: recreation sws_ctx echouee\n", s->index);
            return -1;
        }
    }

    /* 5. Relancer le thread de capture */
    s->capture_running  = 1;
    s->th_video_capture = (HANDLE)_beginthreadex(NULL, 0, thread_stream_video_capture, s, 0, NULL);
    if (!s->th_video_capture) {
        fprintf(stderr, "[Stream %d] Switch: relancement thread capture echoue\n", s->index);
        return -1;
    }

    fprintf(stderr, "[Stream %d] Switch vers '%s' effectue\n", s->index, new_src->label);
    return 0;
}

/* ================================================================== *
 *  API publique
 * ================================================================== */

CASTOR_CORE_API CastorRecorder* recorder_create(const RecorderConfig* config) {
    if (!config || config->num_streams < 1 || config->num_streams > CASTOR_MAX_STREAMS)
        return NULL;

    CastorRecorder* rec = (CastorRecorder*)calloc(1, sizeof(CastorRecorder));
    if (!rec) return NULL;

    rec->config      = *config;
    rec->num_streams = config->num_streams;
    for (int i = 0; i < rec->num_streams; i++) {
        rec->streams[i].config   = config->streams[i];
        rec->streams[i].recorder = rec;
        rec->streams[i].index    = i;
    }
    return rec;
}

CASTOR_CORE_API int recorder_start(CastorRecorder* rec) {
    /* Initialiser chaque stream */
    for (int i = 0; i < rec->num_streams; i++) {
        if (stream_init(&rec->streams[i]) < 0) {
            fprintf(stderr, "[Recorder] Echec init stream %d — arret\n", i);
            for (int j = 0; j < i; j++)
                stream_cleanup(&rec->streams[j]);
            return -1;
        }
    }

    rec->running = 1;

    /* Lancer les threads de chaque stream */
    for (int i = 0; i < rec->num_streams; i++) {
        stream_start_threads(&rec->streams[i]);
        StreamState* s = &rec->streams[i];
        if (!s->th_video_capture || !s->th_video_encode || !s->th_audio) {
            fprintf(stderr, "[Recorder] Echec lancement threads stream %d — arret\n", i);
            rec->running = 0;
            for (int j = 0; j < rec->num_streams; j++) {
                stream_stop_threads(&rec->streams[j]);
                stream_cleanup(&rec->streams[j]);
            }
            return -1;
        }
    }

    return 0;
}

CASTOR_CORE_API void recorder_stop(CastorRecorder* rec) {
    rec->running = 0;

    for (int i = 0; i < rec->num_streams; i++) {
        stream_stop_threads(&rec->streams[i]);
        stream_cleanup(&rec->streams[i]);
    }
}

CASTOR_CORE_API void recorder_destroy(CastorRecorder* rec) {
    if (rec) free(rec);
}

CASTOR_CORE_API int recorder_switch_video_source(CastorRecorder*          rec,
                                                   int                      stream_index,
                                                   const CaptureSourceInfo* new_src) {
    if (!rec || stream_index < 0 || stream_index >= rec->num_streams || !new_src)
        return -1;

    return stream_apply_source_switch(&rec->streams[stream_index], new_src);
}
