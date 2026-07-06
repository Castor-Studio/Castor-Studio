#include "Recorder.h"
#include "FileCapture.h"
#include "VideoEncode.h"
#include "AudioEncode.h"
#include "output/output.h"
#include "output/file_output.h"
#include "output/rtmp_output.h"

#include <windows.h>
#include <process.h>
#include <stdlib.h>
#include <stdio.h>
#include <string.h>
#include <libavutil/frame.h>
#include <libswscale/swscale.h>

/* ================================================================== *
 *  Etat interne d'un stream
 * ================================================================== */
typedef struct CastorRecorder CastorRecorder;

typedef struct {
    StreamConfig        config;

    VideoCaptureContext vctx;
    AudioCaptureContext actx;
    FileCaptureContext* file_capture;  /* non-NULL quand vidéo et audio partagent un seul fichier */
    VideoEncoder        venc;
    AudioEncoder        aenc;

    /* Sortie active.
     *
     * Point d'extension pour record + stream simultanement :
     * remplacer par :
     *   CastorOutput* outputs[CASTOR_MAX_OUTPUTS];
     *   int           num_outputs;
     * et envoyer chaque paquet a tous les outputs dans output_write_packet. */
    CastorOutput*       output;

    AVFrame*            last_video_frame;
    CRITICAL_SECTION    frame_lock;

    /* Verrous separes pour video et audio : le muxer (Muxer.c) a deja sa
     * propre CRITICAL_SECTION pour serialiser l'ecriture reseau elle-meme.
     * Avant, un seul "output_lock" englobait encodage+ecriture pour les
     * deux flux : si l'ecriture RTMP video bloquait (paquet cle, reseau
     * lent), le thread audio restait bloque sur le meme verrou et devait
     * ensuite rattraper le retard d'un coup avec du silence — d'ou les
     * saccades audio pendant le stream. */
    CRITICAL_SECTION    video_lock;
    CRITICAL_SECTION    audio_lock;

    HANDLE              th_video_capture;
    HANDLE              th_video_encode;
    HANDLE              th_audio;

    volatile int        capture_running;
    volatile int        first_frame_ready;
    int                 initialized;
    CastorRecorder*     recorder;
    int                 index;
} StreamState;

struct CastorRecorder {
    RecorderConfig  config;
    StreamState     streams[CASTOR_MAX_STREAMS];
    int             num_streams;
    volatile int    running;
};

/* ================================================================== *
 *  Factory d'output selon OutputConfig
 * ================================================================== */
static CastorOutput* output_create_from_config(const OutputConfig* cfg)
{
    switch (cfg->type) {
    case CASTOR_OUTPUT_FILE:
        return file_output_create(cfg->destination);

    case CASTOR_OUTPUT_RTMP: {
        RtmpOutputConfig rcfg;
        memcpy(rcfg.url, cfg->destination, sizeof(rcfg.url));
        rcfg.video_bitrate_kbps = cfg->video_bitrate_kbps;
        rcfg.audio_bitrate_kbps = cfg->audio_bitrate_kbps;
        rcfg.gop_seconds        = cfg->gop_seconds;
        return rtmp_output_create(&rcfg);
    }

    default:
        fprintf(stderr, "[Recorder] type d'output inconnu : %d\n", cfg->type);
        return NULL;
    }
}

/* ================================================================== *
 *  Threads par stream
 * ================================================================== */

static unsigned __stdcall thread_stream_video_capture(void* arg) {
    StreamState* s = (StreamState*)arg;
    while (s->recorder->running && s->capture_running) {
        AVFrame* f = s->file_capture
            ? file_capture_next_video_frame(s->file_capture)
            : video_capture_next_frame(&s->vctx);
        if (f) {
            EnterCriticalSection(&s->frame_lock);
            if (s->last_video_frame) av_frame_free(&s->last_video_frame);
            s->last_video_frame = f;
            LeaveCriticalSection(&s->frame_lock);
        }
    }
    return 0;
}

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

        if (elapsed < next_frame_time) { Sleep(1); continue; }
        next_frame_time += frame_interval;

        EnterCriticalSection(&s->frame_lock);
        AVFrame* vframe = s->last_video_frame
                          ? av_frame_clone(s->last_video_frame)
                          : NULL;
        LeaveCriticalSection(&s->frame_lock);

        if (vframe) {
            /* Estampille l'horloge murale en microsecondes.
             * video_encoder_encode_frame utilisera cette valeur pour calculer
             * enc->frame->pts via av_rescale_q, garantissant une vitesse de
             * lecture 1:1 meme si l'encodeur (libvpx-vp9) est plus lent que
             * le fps cible (ex : 11fps effectif pour une cible de 60fps). */
            vframe->pts = (int64_t)(elapsed * 1000000.0);
            EnterCriticalSection(&s->video_lock);
            int enc_ret = video_encoder_encode_frame(&s->venc, vframe, s->output);
            LeaveCriticalSection(&s->video_lock);
            av_frame_free(&vframe);
            if (enc_ret < 0) {
                /* Erreur fatale (connexion perdue, disque plein, ...) : inutile
                 * de continuer a encoder. Le recorder sera arrete par l'utilisateur
                 * ou a la fermeture de l'application. */
                fprintf(stderr, "[Stream %d] Erreur fatale encode/write (%d) — thread video stoppe\n",
                        s->index, enc_ret);
                break;
            }
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

static unsigned __stdcall thread_stream_audio(void* arg) {
    StreamState* s = (StreamState*)arg;

    const int sample_rate = s->actx.sample_rate > 0 ? s->actx.sample_rate : 48000;
    const int channels    = s->actx.channels;

    /* Pour les sources live (WASAPI / loopback), on draine l'audio buffere
     * en attente du premier frame video, pour eviter d'encoder de l'audio
     * "perime" capturé avant que l'encodeur soit pret.
     * Pour les sources fichier (seules ou partagées via FileCapture), ce drain
     * consommerait du contenu depuis le debut du fichier sans avancer la video,
     * créant un décalage A/V permanent. On saute donc le drain. */
    if (!s->file_capture && s->config.audio_src.type != AUDIO_SOURCE_FILE) {
        while (s->recorder->running && !s->first_frame_ready) {
            AVFrame* drain = audio_capture_next_frame(&s->actx);
            if (drain) av_frame_free(&drain);
        }
    }
    if (!s->recorder->running) return 0;

    LARGE_INTEGER freq, t0;
    QueryPerformanceFrequency(&freq);
    QueryPerformanceCounter(&t0);

    int64_t submitted  = 0;
    int     in_silence = 0;

    /* Tolerance au jitter avant d'injecter du silence.
     *
     * WASAPI loopback ne livre AUCUN paquet quand rien n'est rendu : c'est
     * le seul cas ou il faut combler avec du silence pour tenir la synchro
     * A/V. Un paquet simplement en retard (charge CPU, ordonnanceur) ne
     * doit PAS declencher d'injection : ses samples arrivent en rafale
     * juste apres et l'insertion decalait tout le flux reel de la duree
     * du retard, a chaque a-coup — c'etait la cause des saccades audio. */
    const int64_t silence_gap_min = (int64_t)sample_rate / 10;  /* 100 ms */

    while (s->recorder->running) {
        AVFrame* aframe = s->file_capture
            ? file_capture_next_audio_frame(s->file_capture)
            : audio_capture_next_frame(&s->actx);

        LARGE_INTEGER t_now;
        QueryPerformanceCounter(&t_now);
        double  elapsed  = (double)(t_now.QuadPart - t0.QuadPart) / freq.QuadPart;
        int64_t expected = (int64_t)(elapsed * sample_rate);

        int     wasapi_samples = aframe ? aframe->nb_samples : 0;
        int64_t gap = expected - submitted - wasapi_samples;
        if (gap < 0) gap = 0;

        /* Silence uniquement si la capture est reellement muette : aucun
         * paquet pendant cette iteration (~20 ms) ET deficit au-dela du
         * seuil. Une fois en periode silencieuse, on complete a chaque
         * iteration (in_silence) pour produire un flux regulier jusqu'au
         * retour de vrais samples. */
        int fill_silence = (aframe == NULL) &&
                           (in_silence ? gap > 0 : gap > silence_gap_min);

        if (fill_silence) {
            AVFrame* silence = av_frame_alloc();
            silence->format      = AV_SAMPLE_FMT_FLTP;
            silence->sample_rate = sample_rate;
            silence->nb_samples  = (int)gap;
            av_channel_layout_default(&silence->ch_layout, channels);
            if (av_frame_get_buffer(silence, 0) == 0) {
                for (int ch = 0; ch < silence->ch_layout.nb_channels; ch++)
                    memset(silence->data[ch], 0, silence->nb_samples * sizeof(float));
                EnterCriticalSection(&s->audio_lock);
                audio_encoder_encode_frame(&s->aenc, silence, s->output);
                LeaveCriticalSection(&s->audio_lock);
                submitted += gap;
                in_silence = 1;
            }
            av_frame_free(&silence);
        }

        if (aframe) {
            in_silence = 0;
            EnterCriticalSection(&s->audio_lock);
            audio_encoder_encode_frame(&s->aenc, aframe, s->output);
            LeaveCriticalSection(&s->audio_lock);
            submitted += wasapi_samples;
            av_frame_free(&aframe);
        } else if (!fill_silence) {
            /* Rien a encoder cette iteration. WASAPI a deja attendu ~20 ms
             * en interne, mais une source fichier en EOF (queue fermee)
             * retourne NULL instantanement : eviter de spinner le CPU. */
            Sleep(1);
        }
    }
    return 0;
}

/* ================================================================== *
 *  Init / cleanup d'un stream
 * ================================================================== */

/* Codes de retour de stream_init (et recorder_start) :
 *  -10 : video_capture_init_source a echoue
 *  -11 : dimensions video nulles apres capture init
 *  -12 : audio_capture_init_source a echoue
 *  -20 : codec video introuvable (libvpx-vp9 / libx264 absent)
 *  -21 : avcodec_open2 video echoue
 *  -22 : sws_getContext echoue
 *  -25 : codec audio introuvable (libopus / AAC absent)
 *  -26 : avcodec_open2 audio echoue
 *  -27 : av_audio_fifo_alloc echoue
 *  -30 : creation de l'output echouee (chemin invalide, RTMP inaccessible)
 *  -31 : output_add_*_stream ou output_write_header echoue */
static int stream_init(StreamState* s) {
    /* Redirect stderr vers un fichier pour rendre les logs natifs visibles.
     * Fichier cree dans le repertoire de travail de l'application. */
    {
        static int log_opened = 0;
        if (!log_opened) {
            if (freopen("castor_debug.log", "w", stderr))
                log_opened = 1;
        }
    }

    /* Détection : même fichier pour vidéo et audio → FileCapture partagé */
    int both_file = (s->config.video_src.type == CAPTURE_SOURCE_FILE &&
                     s->config.audio_src.type  == AUDIO_SOURCE_FILE   &&
                     strcmp(s->config.video_src.symbolic_link,
                            s->config.audio_src.device_id) == 0);

    if (both_file) {
        s->file_capture = file_capture_create(
            s->config.video_src.symbolic_link,
            s->config.video_src.index != 0);
        if (!s->file_capture) {
            fprintf(stderr, "[Stream %d] FileCapture: creation echouee\n", s->index);
            return -10;
        }
        if (!file_capture_has_video(s->file_capture) ||
            !file_capture_has_audio(s->file_capture)) {
            fprintf(stderr, "[Stream %d] FileCapture: flux A/V incomplets dans '%s'\n",
                    s->index, s->config.video_src.symbolic_link);
            file_capture_destroy(&s->file_capture);
            return -10;
        }
        s->vctx.width       = file_capture_width(s->file_capture);
        s->vctx.height      = file_capture_height(s->file_capture);
        s->actx.sample_rate = file_capture_sample_rate(s->file_capture);
        s->actx.channels    = file_capture_channels(s->file_capture);
        fprintf(stderr, "[Stream %d] FileCapture OK — %dx%d audio %dHz/%dch\n",
                s->index, s->vctx.width, s->vctx.height,
                s->actx.sample_rate, s->actx.channels);
    } else {
        if (video_capture_init_source(&s->vctx, &s->config.video_src) < 0) {
            fprintf(stderr, "[Stream %d] Init capture video echouee\n", s->index);
            return -10;
        }
        if (s->vctx.width <= 0 || s->vctx.height <= 0) {
            fprintf(stderr, "[Stream %d] Dimensions invalides apres init capture: %dx%d\n",
                    s->index, s->vctx.width, s->vctx.height);
            video_capture_cleanup(&s->vctx);
            return -11;
        }
        fprintf(stderr, "[Stream %d] Capture video OK — %dx%d (type=%d)\n",
                s->index, s->vctx.width, s->vctx.height, s->config.video_src.type);

        if (audio_capture_init_source(&s->actx, &s->config.audio_src) < 0) {
            fprintf(stderr, "[Stream %d] Init capture audio echouee\n", s->index);
            video_capture_cleanup(&s->vctx);
            return -12;
        }
    }
    if (s->actx.channels > 2) s->actx.channels = 2;

    /* Encoder video — config selon le type d'output */
    VideoEncoderConfig vcfg;
    AudioEncoderConfig acfg;

    if (s->config.output.type == CASTOR_OUTPUT_RTMP) {
        vcfg = video_encoder_config_rtmp(s->config.output.video_bitrate_kbps,
                                         s->config.output.gop_seconds);
        vcfg.video_codec        = CASTOR_VCODEC_H264; /* RTMP = H264 uniquement */
        acfg.audio_bitrate_kbps = s->config.output.audio_bitrate_kbps > 0
                                  ? s->config.output.audio_bitrate_kbps : 128;
        acfg.audio_codec        = CASTOR_ACODEC_AAC;  /* RTMP = AAC uniquement  */
    } else {
        /* Fichier : CBR si bitrate specifie, CRF sinon */
        if (s->config.output.video_bitrate_kbps > 0) {
            vcfg                    = video_encoder_config_default(); /* zero-init */
            vcfg.cbr                = 1;
            vcfg.video_bitrate_kbps = s->config.output.video_bitrate_kbps;
            vcfg.gop_seconds        = s->config.output.gop_seconds > 0
                                      ? s->config.output.gop_seconds : 2;
        } else {
            vcfg = video_encoder_config_default();
        }
        vcfg.video_codec        = s->config.output.video_codec;
        acfg.audio_bitrate_kbps = s->config.output.audio_bitrate_kbps > 0
                                  ? s->config.output.audio_bitrate_kbps : 128;
        acfg.audio_codec        = s->config.output.audio_codec;
    }

    /* Dimensions de sortie : output_width/height si specifie, sinon capture native. */
    const int out_w = (s->config.output.output_width  > 0)
                      ? s->config.output.output_width  : s->vctx.width;
    const int out_h = (s->config.output.output_height > 0)
                      ? s->config.output.output_height : s->vctx.height;

    /* Dimensions source pour sws (redimensionnement capture → sortie). */
    vcfg.src_width  = s->vctx.width;
    vcfg.src_height = s->vctx.height;

    /* CRF selon le quality_index : 0=haute 1=bonne 2=basse.
     * Valeurs per-codec : H264 {18,23,28} / VP9 {25,33,40}. */
    if (!vcfg.cbr) {
        static const int h264_crf[3] = {18, 23, 28};
        static const int vp9_crf [3] = {25, 33, 40};
        int qi = s->config.output.quality_index;
        if (qi < 0 || qi > 2) qi = 1;
        vcfg.crf = (vcfg.video_codec == CASTOR_VCODEC_VP9)
                   ? vp9_crf[qi] : h264_crf[qi];
    }

    int enc_ret = video_encoder_init_ex(&s->venc,
                                        out_w, out_h,
                                        s->recorder->config.fps, &vcfg);
    if (enc_ret < 0) {
        fprintf(stderr, "[Stream %d] Init encodeur video echoue (code %d)\n", s->index, enc_ret);
        video_capture_cleanup(&s->vctx);
        audio_capture_cleanup(&s->actx);
        return enc_ret; /* -20 / -21 / -22 */
    }

    int aenc_ret = audio_encoder_init_ex(&s->aenc, s->actx.sample_rate, &acfg);
    if (aenc_ret < 0) {
        fprintf(stderr, "[Stream %d] Init encodeur audio echoue (code %d)\n", s->index, aenc_ret);
        video_encoder_cleanup(&s->venc, NULL);
        video_capture_cleanup(&s->vctx);
        audio_capture_cleanup(&s->actx);
        return aenc_ret; /* -25 / -26 / -27 */
    }

    /* Creer l'output (fichier ou RTMP) */
    s->output = output_create_from_config(&s->config.output);
    if (!s->output) {
        fprintf(stderr, "[Stream %d] Creation output echouee\n", s->index);
        video_encoder_cleanup(&s->venc, NULL);
        audio_encoder_cleanup(&s->aenc, NULL);
        video_capture_cleanup(&s->vctx);
        audio_capture_cleanup(&s->actx);
        return -30;
    }

    if (output_add_video_stream(s->output, s->venc.ctx) < 0 ||
        output_add_audio_stream(s->output, s->aenc.ctx) < 0 ||
        output_write_header(s->output) < 0) {
        fprintf(stderr, "[Stream %d] Init output echouee\n", s->index);
        output_close(s->output);
        output_destroy(&s->output);
        video_encoder_cleanup(&s->venc, NULL);
        audio_encoder_cleanup(&s->aenc, NULL);
        video_capture_cleanup(&s->vctx);
        audio_capture_cleanup(&s->actx);
        return -31;
    }

    InitializeCriticalSection(&s->frame_lock);
    InitializeCriticalSection(&s->video_lock);
    InitializeCriticalSection(&s->audio_lock);
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
    /* Fermer les queues FileCapture pour débloquer les threads en attente
     * sur file_capture_next_*_frame avant le WaitForSingleObject. */
    if (s->file_capture)
        file_capture_signal_stop(s->file_capture);
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
    DeleteCriticalSection(&s->video_lock);
    DeleteCriticalSection(&s->audio_lock);
    if (s->last_video_frame) {
        av_frame_free(&s->last_video_frame);
        s->last_video_frame = NULL;
    }

    video_encoder_cleanup(&s->venc, s->output);
    audio_encoder_cleanup(&s->aenc, s->output);
    output_close(s->output);
    output_destroy(&s->output);

    if (s->file_capture) {
        file_capture_destroy(&s->file_capture);
    } else {
        video_capture_cleanup(&s->vctx);
        audio_capture_cleanup(&s->actx);
    }

    s->initialized = 0;
}

static int stream_apply_source_switch(StreamState* s, const CaptureSourceInfo* new_src) {
    if (!s || !s->initialized || !new_src)
        return -1;

    s->capture_running = 0;
    if (s->th_video_capture) {
        WaitForSingleObject(s->th_video_capture, 5000);
        CloseHandle(s->th_video_capture);
        s->th_video_capture = NULL;
    }

    EnterCriticalSection(&s->frame_lock);
    if (s->last_video_frame) {
        av_frame_free(&s->last_video_frame);
        s->last_video_frame = NULL;
    }
    s->first_frame_ready = 0;
    LeaveCriticalSection(&s->frame_lock);

    /* Si on était en mode FileCapture, le détruire et ré-init l'audio séparément */
    if (s->file_capture) {
        file_capture_destroy(&s->file_capture);
        memset(&s->actx, 0, sizeof(s->actx));
        if (audio_capture_init_source(&s->actx, &s->config.audio_src) < 0)
            fprintf(stderr, "[Stream %d] Switch: re-init audio echouee\n", s->index);
    }

    video_capture_cleanup(&s->vctx);
    memset(&s->vctx, 0, sizeof(s->vctx));

    s->config.video_src = *new_src;
    if (video_capture_init_source(&s->vctx, &s->config.video_src) < 0) {
        fprintf(stderr, "[Stream %d] Switch: init nouvelle source echouee\n", s->index);
        return -1;
    }

    EnterCriticalSection(&s->video_lock);
    struct SwsContext* new_sws_ctx = sws_getContext(
        s->vctx.width,      s->vctx.height,      AV_PIX_FMT_BGRA,
        s->venc.ctx->width, s->venc.ctx->height,  AV_PIX_FMT_YUV420P,
        SWS_BILINEAR, NULL, NULL, NULL
    );
    if (!new_sws_ctx) {
        LeaveCriticalSection(&s->video_lock);
        fprintf(stderr, "[Stream %d] Switch: recreation sws_ctx echouee\n", s->index);
        video_capture_cleanup(&s->vctx);
        return -1;
    }
    sws_freeContext(s->venc.sws_ctx);
    s->venc.sws_ctx = new_sws_ctx;
    LeaveCriticalSection(&s->video_lock);

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
    if (!rec) return -1;

    for (int i = 0; i < rec->num_streams; i++) {
        if (stream_init(&rec->streams[i]) < 0) {
            fprintf(stderr, "[Recorder] Echec init stream %d — arret\n", i);
            for (int j = 0; j < i; j++) stream_cleanup(&rec->streams[j]);
            return -1;
        }
    }

    rec->running = 1;

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
    if (!rec) return;

    rec->running = 0;
    for (int i = 0; i < rec->num_streams; i++) {
        stream_stop_threads(&rec->streams[i]);
        stream_cleanup(&rec->streams[i]);
    }
}

CASTOR_CORE_API void recorder_destroy(CastorRecorder* rec) {
    if (rec) free(rec);
}

CASTOR_CORE_API int recorder_switch_video_source(CastorRecorder* rec, int stream_index,
                                                  const CaptureSourceInfo* new_src) {
    if (!rec || stream_index < 0 || stream_index >= rec->num_streams || !new_src)
        return -1;
    return stream_apply_source_switch(&rec->streams[stream_index], new_src);
}
