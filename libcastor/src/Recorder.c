#include "Recorder.h"
#include "VideoEncode.h"
#include "AudioEncode.h"
#include "Muxer.h"

#include <windows.h>
#include <process.h>
#include <stdlib.h>
#include <stdio.h>
#include <libavutil/frame.h>

/* ------------------------------------------------------------------ *
 *  Structure interne (opaque dans le header)
 * ------------------------------------------------------------------ */
struct CastorRecorder {
    RecorderConfig      config;

    VideoCaptureContext vctx;
    AudioCaptureContext actx;
    VideoEncoder        venc;
    AudioEncoder        aenc;
    CastorMuxer         mux;

    AVFrame*            last_video_frame;
    CRITICAL_SECTION    frame_lock;
    volatile int        running;

    HANDLE              th_video_capture;
    HANDLE              th_video_encode;
    HANDLE              th_audio;
};

/* ------------------------------------------------------------------ *
 *  Thread 1 : capture video
 *  Alimente last_video_frame en continu.
 * ------------------------------------------------------------------ */
static unsigned __stdcall thread_video_capture(void* arg) {
    CastorRecorder* rec = (CastorRecorder*)arg;
    while (rec->running) {
        AVFrame* f = video_capture_next_frame(&rec->vctx);
        if (f) {
            EnterCriticalSection(&rec->frame_lock);
            if (rec->last_video_frame) av_frame_free(&rec->last_video_frame);
            rec->last_video_frame = f;
            LeaveCriticalSection(&rec->frame_lock);
        }
    }
    return 0;
}

/* ------------------------------------------------------------------ *
 *  Thread 2 : encodage video (boucle fps strict)
 *  Lit last_video_frame et encode vers le muxer MP4.
 * ------------------------------------------------------------------ */
static unsigned __stdcall thread_video_encode(void* arg) {
    CastorRecorder* rec = (CastorRecorder*)arg;

    timeBeginPeriod(1);

    LARGE_INTEGER freq, start, now;
    QueryPerformanceFrequency(&freq);
    QueryPerformanceCounter(&start);

    const double frame_interval = 1.0 / rec->config.fps;
    double       next_frame_time = 0.0;
    int          frames_encoded  = 0;

    while (rec->running) {
        QueryPerformanceCounter(&now);
        double elapsed = (double)(now.QuadPart - start.QuadPart) / freq.QuadPart;

        if (elapsed < next_frame_time) {
            Sleep(1);
            continue;
        }
        next_frame_time += frame_interval;

        EnterCriticalSection(&rec->frame_lock);
        AVFrame* vframe = rec->last_video_frame
                          ? av_frame_clone(rec->last_video_frame)
                          : NULL;
        LeaveCriticalSection(&rec->frame_lock);

        if (vframe) {
            video_encoder_encode_frame(&rec->venc, vframe, &rec->mux);
            av_frame_free(&vframe);
            frames_encoded++;
        }
    }

    timeEndPeriod(1);

    QueryPerformanceCounter(&now);
    double total = (double)(now.QuadPart - start.QuadPart) / freq.QuadPart;
    if (total > 0.0)
        fprintf(stderr, "[Recorder] Total: %.2fs — %d frames — fps effectif: %.1f\n",
                total, frames_encoded, frames_encoded / total);

    return 0;
}

/* ------------------------------------------------------------------ *
 *  Thread 3 : capture + encodage audio
 *
 *  Strategrie de synchronisation basee sur l'horloge QPC absolue :
 *  On compare en permanence le nombre de samples soumis au FIFO
 *  (submitted) avec le nombre attendu selon le temps ecoule depuis t0.
 *  Tout ecart est comble par du silence, que WASAPI livre NULL, des
 *  paquets SILENT a rythme irregulier, ou des vraies donnees.
 * ------------------------------------------------------------------ */
static unsigned __stdcall thread_audio(void* arg) {
    CastorRecorder* rec = (CastorRecorder*)arg;

    const int sample_rate = rec->actx.sample_rate > 0 ? rec->actx.sample_rate : 48000;
    const int channels    = rec->actx.channels;

    LARGE_INTEGER freq, t0;
    QueryPerformanceFrequency(&freq);
    QueryPerformanceCounter(&t0);

    int64_t submitted = 0;   /* total de samples envoyes au FIFO de l'encodeur */

    while (rec->running) {
        AVFrame* aframe = audio_capture_next_frame(&rec->actx);

        /* Temps ecoule depuis le debut de l'enregistrement */
        LARGE_INTEGER t_now;
        QueryPerformanceCounter(&t_now);
        double elapsed  = (double)(t_now.QuadPart - t0.QuadPart) / freq.QuadPart;
        int64_t expected = (int64_t)(elapsed * sample_rate);

        /* Samples reels qu'on s'apprete a soumettre (peut etre 0) */
        int wasapi_samples = aframe ? aframe->nb_samples : 0;

        /* Gap = samples de silence a inserer AVANT les samples reels
         * pour que submitted + gap + wasapi_samples == expected */
        int64_t gap = expected - submitted - wasapi_samples;
        if (gap < 0) gap = 0;

        if (gap > 0) {
            AVFrame* silence = av_frame_alloc();
            silence->format      = AV_SAMPLE_FMT_FLTP;
            silence->sample_rate = sample_rate;
            silence->nb_samples  = (int)gap;
            av_channel_layout_default(&silence->ch_layout, channels);
            if (av_frame_get_buffer(silence, 0) == 0) {
                /* av_frame_get_buffer appelle av_malloc (pas av_mallocz) :
                 * les plans peuvent contenir des NaN/Inf -> zero explicitement. */
                for (int ch = 0; ch < silence->ch_layout.nb_channels; ch++)
                    memset(silence->data[ch], 0, silence->nb_samples * sizeof(float));
                audio_encoder_encode_frame(&rec->aenc, silence, &rec->mux);
                submitted += gap;
            }
            av_frame_free(&silence);
        }

        if (aframe) {
            audio_encoder_encode_frame(&rec->aenc, aframe, &rec->mux);
            submitted += wasapi_samples;
            av_frame_free(&aframe);
        }
    }
    return 0;
}

/* ================================================================== *
 *  API publique
 * ================================================================== */

CASTOR_CORE_API CastorRecorder* recorder_create(const RecorderConfig* config) {
    CastorRecorder* rec = (CastorRecorder*)calloc(1, sizeof(CastorRecorder));
    if (!rec) return NULL;
    rec->config = *config;
    return rec;
}

CASTOR_CORE_API int recorder_start(CastorRecorder* rec) {
    /* 1. Init capture video — fournit width/height pour l'encodeur */
    if (video_capture_init_source(&rec->vctx, &rec->config.video_src) < 0) {
        fprintf(stderr, "[Recorder] Init capture video echouee\n");
        return -1;
    }

    /* 2. Init capture audio — fournit sample_rate pour l'encodeur */
    if (audio_capture_init_source(&rec->actx, &rec->config.audio_src) < 0) {
        fprintf(stderr, "[Recorder] Init capture audio echouee\n");
        video_capture_cleanup(&rec->vctx);
        return -1;
    }
    if (rec->actx.channels > 2) rec->actx.channels = 2;

    /* 3. Init encodeurs (codec context seulement, pas de fichier) */
    if (video_encoder_init(&rec->venc, rec->vctx.width, rec->vctx.height, rec->config.fps) < 0) {
        fprintf(stderr, "[Recorder] Init encodeur video echoue\n");
        video_capture_cleanup(&rec->vctx);
        audio_capture_cleanup(&rec->actx);
        return -1;
    }

    if (audio_encoder_init(&rec->aenc, rec->actx.sample_rate) < 0) {
        fprintf(stderr, "[Recorder] Init encodeur audio echoue\n");
        video_encoder_cleanup(&rec->venc, &rec->mux);
        video_capture_cleanup(&rec->vctx);
        audio_capture_cleanup(&rec->actx);
        return -1;
    }

    /* 4. Ouvrir le muxer MP4 et ajouter les deux streams */
    if (muxer_open(&rec->mux, rec->config.output_path) < 0) {
        fprintf(stderr, "[Recorder] muxer_open echoue\n");
        video_encoder_cleanup(&rec->venc, &rec->mux);
        audio_encoder_cleanup(&rec->aenc, &rec->mux);
        video_capture_cleanup(&rec->vctx);
        audio_capture_cleanup(&rec->actx);
        return -1;
    }

    if (muxer_add_video_stream(&rec->mux, rec->venc.ctx) < 0 ||
        muxer_add_audio_stream(&rec->mux, rec->aenc.ctx) < 0) {
        fprintf(stderr, "[Recorder] muxer_add_stream echoue\n");
        muxer_close(&rec->mux);
        video_encoder_cleanup(&rec->venc, &rec->mux);
        audio_encoder_cleanup(&rec->aenc, &rec->mux);
        video_capture_cleanup(&rec->vctx);
        audio_capture_cleanup(&rec->actx);
        return -1;
    }

    if (muxer_write_header(&rec->mux) < 0) {
        fprintf(stderr, "[Recorder] muxer_write_header echoue\n");
        muxer_close(&rec->mux);
        video_encoder_cleanup(&rec->venc, &rec->mux);
        audio_encoder_cleanup(&rec->aenc, &rec->mux);
        video_capture_cleanup(&rec->vctx);
        audio_capture_cleanup(&rec->actx);
        return -1;
    }

    /* 5. Init synchronisation */
    InitializeCriticalSection(&rec->frame_lock);
    rec->running = 1;

    /* 6. Lancer les 3 threads */
    rec->th_video_capture = (HANDLE)_beginthreadex(NULL, 0, thread_video_capture, rec, 0, NULL);
    rec->th_video_encode  = (HANDLE)_beginthreadex(NULL, 0, thread_video_encode,  rec, 0, NULL);
    rec->th_audio         = (HANDLE)_beginthreadex(NULL, 0, thread_audio,          rec, 0, NULL);

    if (!rec->th_video_capture || !rec->th_video_encode || !rec->th_audio) {
        fprintf(stderr, "[Recorder] Creation d'un thread echouee\n");
        recorder_stop(rec);
        return -1;
    }

    return 0;
}

CASTOR_CORE_API void recorder_stop(CastorRecorder* rec) {
    rec->running = 0;

    HANDLE threads[3] = { rec->th_video_capture, rec->th_video_encode, rec->th_audio };
    for (int i = 0; i < 3; i++)
        if (threads[i]) WaitForSingleObject(threads[i], 5000);
    for (int i = 0; i < 3; i++)
        if (threads[i]) CloseHandle(threads[i]);

    rec->th_video_capture = NULL;
    rec->th_video_encode  = NULL;
    rec->th_audio         = NULL;

    DeleteCriticalSection(&rec->frame_lock);
    if (rec->last_video_frame) {
        av_frame_free(&rec->last_video_frame);
        rec->last_video_frame = NULL;
    }

    /* Flush encodeurs puis fermer le muxer (ecrit le trailer MP4) */
    video_encoder_cleanup(&rec->venc, &rec->mux);
    audio_encoder_cleanup(&rec->aenc, &rec->mux);
    muxer_close(&rec->mux);

    video_capture_cleanup(&rec->vctx);
    audio_capture_cleanup(&rec->actx);
}

CASTOR_CORE_API void recorder_destroy(CastorRecorder* rec) {
    if (rec) free(rec);
}
