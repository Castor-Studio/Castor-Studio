/* windows.h doit précéder les headers FFmpeg sur MSVC/Windows */
#ifdef _WIN32
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>
#endif

#include "FileCapture.h"
#include "utils/sync_clock.h"

#include <libavformat/avformat.h>
#include <libavcodec/avcodec.h>
#include <libswscale/swscale.h>
#include <libswresample/swresample.h>
#include <libavutil/time.h>
#include <libavutil/mathematics.h>
#include <libavutil/channel_layout.h>

#include <pthread.h>
#include <stdlib.h>
#include <string.h>
#include <stdio.h>

/* ================================================================== *
 *  Queue de frames thread-safe
 *
 *  queue_push : non-bloquant — si la queue est pleine la plus ancienne
 *               frame est droppée, ce qui évite tout deadlock dans le
 *               thread demux (qui produit vidéo et audio depuis la même
 *               boucle et ne peut pas se bloquer sur un seul flux).
 *  queue_pop  : bloquant — attend jusqu'à ce qu'une frame soit dispo
 *               ou que la queue soit fermée.
 * ================================================================== */

#define VIDEO_QUEUE_MAX 4
#define AUDIO_QUEUE_MAX 16

typedef struct FrameNode {
    AVFrame*          frame;
    struct FrameNode* next;
} FrameNode;

typedef struct {
    FrameNode*      head;
    FrameNode*      tail;
    int             count;
    int             max_size;
    int             closed;
    pthread_mutex_t mutex;
    pthread_cond_t  not_empty;
} FrameQueue;

static void queue_init(FrameQueue* q, int max_size) {
    memset(q, 0, sizeof(*q));
    q->max_size = max_size;
    pthread_mutex_init(&q->mutex, NULL);
    pthread_cond_init(&q->not_empty, NULL);
}

static void queue_drain_locked(FrameQueue* q) {
    FrameNode* n = q->head;
    while (n) {
        FrameNode* next = n->next;
        av_frame_free(&n->frame);
        free(n);
        n = next;
    }
    q->head = q->tail = NULL;
    q->count = 0;
}

static void queue_destroy(FrameQueue* q) {
    pthread_mutex_lock(&q->mutex);
    queue_drain_locked(q);
    pthread_mutex_unlock(&q->mutex);
    pthread_mutex_destroy(&q->mutex);
    pthread_cond_destroy(&q->not_empty);
}

static void queue_push(FrameQueue* q, AVFrame* frame) {
    pthread_mutex_lock(&q->mutex);
    if (q->closed) {
        pthread_mutex_unlock(&q->mutex);
        av_frame_free(&frame);
        return;
    }
    if (q->count >= q->max_size) {
        FrameNode* old = q->head;
        q->head = old->next;
        if (!q->head) q->tail = NULL;
        q->count--;
        av_frame_free(&old->frame);
        free(old);
    }
    FrameNode* node = (FrameNode*)malloc(sizeof(FrameNode));
    node->frame = frame;
    node->next  = NULL;
    if (q->tail) q->tail->next = node;
    else         q->head       = node;
    q->tail = node;
    q->count++;
    pthread_cond_signal(&q->not_empty);
    pthread_mutex_unlock(&q->mutex);
}

static AVFrame* queue_pop(FrameQueue* q) {
    pthread_mutex_lock(&q->mutex);
    while (q->count == 0 && !q->closed)
        pthread_cond_wait(&q->not_empty, &q->mutex);
    if (q->count == 0) {
        pthread_mutex_unlock(&q->mutex);
        return NULL;
    }
    FrameNode* node = q->head;
    q->head = node->next;
    if (!q->head) q->tail = NULL;
    q->count--;
    AVFrame* frame = node->frame;
    free(node);
    pthread_mutex_unlock(&q->mutex);
    return frame;
}

static void queue_close(FrameQueue* q) {
    pthread_mutex_lock(&q->mutex);
    q->closed = 1;
    pthread_cond_broadcast(&q->not_empty);
    pthread_mutex_unlock(&q->mutex);
}

/* ================================================================== *
 *  Contexte interne — opaque depuis l'extérieur
 * ================================================================== */
struct FileCaptureContext {
    AVFormatContext* fmt_ctx;

    int             video_stream_idx;
    AVCodecContext* video_codec_ctx;
    struct SwsContext* video_sws_ctx;
    FrameQueue      video_queue;

    int             audio_stream_idx;
    AVCodecContext* audio_codec_ctx;
    SwrContext*     audio_swr_ctx;
    FrameQueue      audio_queue;
    int64_t         next_audio_pts;  /* monotone, ne se réinitialise jamais */

    int    width, height;
    int    sample_rate, channels;

    int     loop;
    int64_t start_wall_us;
    int64_t first_pts_us;
    int     first_pts_captured;
    int64_t last_pts_us;
    int64_t pts_offset_us;

    pthread_t    thread;
    volatile int running;
};

/* ================================================================== *
 *  Décodage paquet → queue
 * ================================================================== */

static void process_video_packet(struct FileCaptureContext* ctx, AVPacket* pkt) {
    if (avcodec_send_packet(ctx->video_codec_ctx, pkt) < 0) return;

    AVFrame* decoded = av_frame_alloc();
    while (1) {
        int ret = avcodec_receive_frame(ctx->video_codec_ctx, decoded);
        if (ret == AVERROR(EAGAIN) || ret == AVERROR_EOF) break;
        if (ret < 0) break;

        if (!ctx->video_sws_ctx) {
            ctx->video_sws_ctx = sws_getContext(
                decoded->width, decoded->height,
                (enum AVPixelFormat)decoded->format,
                decoded->width, decoded->height,
                AV_PIX_FMT_BGRA,
                SWS_BILINEAR, NULL, NULL, NULL);
        }

        AVFrame* out = av_frame_alloc();
        out->format = AV_PIX_FMT_BGRA;
        out->width  = decoded->width;
        out->height = decoded->height;

        if (av_frame_get_buffer(out, 0) == 0) {
            sws_scale(ctx->video_sws_ctx,
                      decoded->data, decoded->linesize, 0, decoded->height,
                      out->data, out->linesize);
            out->pts = av_gettime_relative();
            queue_push(&ctx->video_queue, out);
        } else {
            av_frame_free(&out);
        }
        av_frame_unref(decoded);
    }
    av_frame_free(&decoded);
}

static void process_audio_packet(struct FileCaptureContext* ctx, AVPacket* pkt) {
    if (avcodec_send_packet(ctx->audio_codec_ctx, pkt) < 0) return;

    AVFrame* decoded = av_frame_alloc();
    while (1) {
        int ret = avcodec_receive_frame(ctx->audio_codec_ctx, decoded);
        if (ret == AVERROR(EAGAIN) || ret == AVERROR_EOF) break;
        if (ret < 0) break;

        if (!ctx->audio_swr_ctx) {
            AVChannelLayout stereo;
            av_channel_layout_default(&stereo, 2);
            swr_alloc_set_opts2(&ctx->audio_swr_ctx,
                &stereo,             AV_SAMPLE_FMT_FLTP, 48000,
                &decoded->ch_layout, (enum AVSampleFormat)decoded->format,
                decoded->sample_rate, 0, NULL);
            swr_init(ctx->audio_swr_ctx);
        }

        int out_samples = (int)av_rescale_rnd(
            swr_get_delay(ctx->audio_swr_ctx, decoded->sample_rate) + decoded->nb_samples,
            48000, decoded->sample_rate, AV_ROUND_UP);

        AVFrame* out = av_frame_alloc();
        out->format      = AV_SAMPLE_FMT_FLTP;
        out->sample_rate = 48000;
        out->nb_samples  = out_samples;
        av_channel_layout_default(&out->ch_layout, 2);
        av_frame_get_buffer(out, 0);

        int converted = swr_convert(ctx->audio_swr_ctx,
            out->data, out_samples,
            (const uint8_t**)decoded->data, decoded->nb_samples);
        out->nb_samples = converted;
        out->pts        = ctx->next_audio_pts;
        ctx->next_audio_pts += converted;

        queue_push(&ctx->audio_queue, out);
        av_frame_unref(decoded);
    }
    av_frame_free(&decoded);
}

/* ================================================================== *
 *  Thread demux
 *
 *  Cadence la sortie sur le PTS natif du fichier (rate-limiting).
 *  Un seul AVFormatContext pour audio et vidéo : pas de dérive
 *  d'horloge entre les deux flux.
 * ================================================================== */
static void* demux_thread(void* arg) {
    struct FileCaptureContext* ctx = (struct FileCaptureContext*)arg;
    AVPacket* pkt = av_packet_alloc();

    while (ctx->running) {
        int ret = av_read_frame(ctx->fmt_ctx, pkt);
        if (ret < 0) {
            if (ret == AVERROR_EOF && ctx->loop) {
                int64_t frame_dur_us = 33333;
                if (ctx->video_stream_idx >= 0) {
                    AVStream* vst = ctx->fmt_ctx->streams[ctx->video_stream_idx];
                    if (vst->avg_frame_rate.num > 0 && vst->avg_frame_rate.den > 0)
                        frame_dur_us = av_rescale_q(
                            1, av_inv_q(vst->avg_frame_rate), AV_TIME_BASE_Q);
                }
                ctx->pts_offset_us +=
                    ctx->last_pts_us - ctx->first_pts_us + frame_dur_us;

                av_seek_frame(ctx->fmt_ctx, -1, 0, AVSEEK_FLAG_BACKWARD);
                if (ctx->video_codec_ctx) avcodec_flush_buffers(ctx->video_codec_ctx);
                if (ctx->audio_codec_ctx) {
                    avcodec_flush_buffers(ctx->audio_codec_ctx);
                    if (ctx->audio_swr_ctx)
                        swr_convert(ctx->audio_swr_ctx, NULL, 0, NULL, 0);
                }
                continue;
            }
            break;
        }

        /* Rate-limiting : cadencer sur le PTS du paquet */
        int64_t pkt_pts = (pkt->pts != AV_NOPTS_VALUE) ? pkt->pts
                        : (pkt->dts != AV_NOPTS_VALUE) ? pkt->dts
                        : AV_NOPTS_VALUE;

        if (pkt_pts != AV_NOPTS_VALUE) {
            AVStream* st   = ctx->fmt_ctx->streams[pkt->stream_index];
            int64_t pts_us = av_rescale_q(pkt_pts, st->time_base, AV_TIME_BASE_Q);

            if (!ctx->first_pts_captured) {
                ctx->first_pts_us      = pts_us;
                ctx->start_wall_us     = av_gettime_relative();
                ctx->first_pts_captured = 1;
            }
            ctx->last_pts_us = pts_us;

            int64_t expected = ctx->start_wall_us
                + (pts_us - ctx->first_pts_us + ctx->pts_offset_us);

            /* Attente en chunks de 10 ms pour rester interruptible */
            while (ctx->running) {
                int64_t now  = av_gettime_relative();
                int64_t wait = expected - now;
                if (wait <= 1000) break;
                av_usleep((unsigned)(wait > 10000 ? 10000 : (unsigned)wait));
            }
            if (!ctx->running) { av_packet_unref(pkt); break; }
        }

        if (pkt->stream_index == ctx->video_stream_idx && ctx->video_codec_ctx)
            process_video_packet(ctx, pkt);
        else if (pkt->stream_index == ctx->audio_stream_idx && ctx->audio_codec_ctx)
            process_audio_packet(ctx, pkt);

        av_packet_unref(pkt);
    }

    av_packet_free(&pkt);
    queue_close(&ctx->video_queue);
    queue_close(&ctx->audio_queue);
    return NULL;
}

/* ================================================================== *
 *  API publique
 * ================================================================== */

CASTOR_CORE_API FileCaptureContext* file_capture_create(const char* path, int loop) {
    struct FileCaptureContext* ctx =
        (struct FileCaptureContext*)calloc(1, sizeof(struct FileCaptureContext));
    if (!ctx) return NULL;

    ctx->loop             = loop;
    ctx->video_stream_idx = -1;
    ctx->audio_stream_idx = -1;
    ctx->sample_rate      = 48000;
    ctx->channels         = 2;

    if (avformat_open_input(&ctx->fmt_ctx, path, NULL, NULL) < 0) {
        fprintf(stderr, "[FileCapture] Impossible d'ouvrir '%s'\n", path);
        free(ctx);
        return NULL;
    }
    if (avformat_find_stream_info(ctx->fmt_ctx, NULL) < 0) {
        fprintf(stderr, "[FileCapture] find_stream_info echoue pour '%s'\n", path);
        avformat_close_input(&ctx->fmt_ctx);
        free(ctx);
        return NULL;
    }

    ctx->video_stream_idx = av_find_best_stream(
        ctx->fmt_ctx, AVMEDIA_TYPE_VIDEO, -1, -1, NULL, 0);
    ctx->audio_stream_idx = av_find_best_stream(
        ctx->fmt_ctx, AVMEDIA_TYPE_AUDIO, -1, -1, NULL, 0);

    if (ctx->video_stream_idx < 0 && ctx->audio_stream_idx < 0) {
        fprintf(stderr, "[FileCapture] Aucun flux A/V dans '%s'\n", path);
        avformat_close_input(&ctx->fmt_ctx);
        free(ctx);
        return NULL;
    }

    if (ctx->video_stream_idx >= 0) {
        AVStream*      vst   = ctx->fmt_ctx->streams[ctx->video_stream_idx];
        const AVCodec* codec = avcodec_find_decoder(vst->codecpar->codec_id);
        if (codec) {
            ctx->video_codec_ctx = avcodec_alloc_context3(codec);
            avcodec_parameters_to_context(ctx->video_codec_ctx, vst->codecpar);
            if (avcodec_open2(ctx->video_codec_ctx, codec, NULL) < 0) {
                avcodec_free_context(&ctx->video_codec_ctx);
                ctx->video_stream_idx = -1;
            } else {
                ctx->width  = ctx->video_codec_ctx->width;
                ctx->height = ctx->video_codec_ctx->height;
            }
        } else {
            ctx->video_stream_idx = -1;
        }
    }

    if (ctx->audio_stream_idx >= 0) {
        AVStream*      ast   = ctx->fmt_ctx->streams[ctx->audio_stream_idx];
        const AVCodec* codec = avcodec_find_decoder(ast->codecpar->codec_id);
        if (codec) {
            ctx->audio_codec_ctx = avcodec_alloc_context3(codec);
            avcodec_parameters_to_context(ctx->audio_codec_ctx, ast->codecpar);
            if (avcodec_open2(ctx->audio_codec_ctx, codec, NULL) < 0) {
                avcodec_free_context(&ctx->audio_codec_ctx);
                ctx->audio_stream_idx = -1;
            }
        } else {
            ctx->audio_stream_idx = -1;
        }
    }

    /* Synchronisation inter-scenes : voir video_capture_init_file (meme logique,
     * ici sur le AVFormatContext partage video+audio). Sans ca, chaque retour
     * sur cette scene rouvre le fichier et il "redemarre" a l'image 0, ce qui
     * desynchronise les scenes fichier entre elles lors des switches. */
    {
        int64_t duration_us = ctx->fmt_ctx->duration;
        if (duration_us <= 0) {
            int ref_idx = ctx->video_stream_idx >= 0 ? ctx->video_stream_idx : ctx->audio_stream_idx;
            AVStream* dur_stream = ctx->fmt_ctx->streams[ref_idx];
            if (dur_stream->duration > 0 && dur_stream->time_base.den > 0)
                duration_us = av_rescale_q(dur_stream->duration, dur_stream->time_base, AV_TIME_BASE_Q);
        }

        if (duration_us > 0) {
            int64_t elapsed_us = av_gettime_relative() - castor_file_sync_epoch_us();
            if (elapsed_us < 0) elapsed_us = 0;

            int64_t seek_target_us = loop ? (elapsed_us % duration_us)
                                           : (elapsed_us < duration_us ? elapsed_us : duration_us);

            if (seek_target_us > 0)
                av_seek_frame(ctx->fmt_ctx, -1, seek_target_us, AVSEEK_FLAG_BACKWARD);
        }
    }

    queue_init(&ctx->video_queue, VIDEO_QUEUE_MAX);
    queue_init(&ctx->audio_queue, AUDIO_QUEUE_MAX);

    ctx->running = 1;
    if (pthread_create(&ctx->thread, NULL, demux_thread, ctx) != 0) {
        fprintf(stderr, "[FileCapture] pthread_create echoue\n");
        ctx->running = 0;
        queue_destroy(&ctx->video_queue);
        queue_destroy(&ctx->audio_queue);
        if (ctx->video_codec_ctx) avcodec_free_context(&ctx->video_codec_ctx);
        if (ctx->audio_codec_ctx) avcodec_free_context(&ctx->audio_codec_ctx);
        avformat_close_input(&ctx->fmt_ctx);
        free(ctx);
        return NULL;
    }

    fprintf(stderr, "[FileCapture] '%s' — %dx%d audio=48kHz/2ch loop=%d\n",
            path, ctx->width, ctx->height, loop);
    return ctx;
}

CASTOR_CORE_API void file_capture_signal_stop(FileCaptureContext* ctx) {
    if (!ctx) return;
    ctx->running = 0;
    queue_close(&ctx->video_queue);
    queue_close(&ctx->audio_queue);
}

CASTOR_CORE_API void file_capture_destroy(FileCaptureContext** pctx) {
    if (!pctx || !*pctx) return;
    struct FileCaptureContext* ctx = *pctx;

    file_capture_signal_stop(ctx);
    pthread_join(ctx->thread, NULL);

    queue_destroy(&ctx->video_queue);
    queue_destroy(&ctx->audio_queue);

    if (ctx->video_sws_ctx)   sws_freeContext(ctx->video_sws_ctx);
    if (ctx->audio_swr_ctx)   swr_free(&ctx->audio_swr_ctx);
    if (ctx->video_codec_ctx) avcodec_free_context(&ctx->video_codec_ctx);
    if (ctx->audio_codec_ctx) avcodec_free_context(&ctx->audio_codec_ctx);
    if (ctx->fmt_ctx)         avformat_close_input(&ctx->fmt_ctx);

    free(ctx);
    *pctx = NULL;
}

CASTOR_CORE_API AVFrame* file_capture_next_video_frame(FileCaptureContext* ctx) {
    return ctx ? queue_pop(&ctx->video_queue) : NULL;
}

CASTOR_CORE_API AVFrame* file_capture_next_audio_frame(FileCaptureContext* ctx) {
    return ctx ? queue_pop(&ctx->audio_queue) : NULL;
}

CASTOR_CORE_API int file_capture_width(FileCaptureContext* ctx)       { return ctx ? ctx->width       : 0;     }
CASTOR_CORE_API int file_capture_height(FileCaptureContext* ctx)      { return ctx ? ctx->height      : 0;     }
CASTOR_CORE_API int file_capture_sample_rate(FileCaptureContext* ctx) { return ctx ? ctx->sample_rate : 48000; }
CASTOR_CORE_API int file_capture_channels(FileCaptureContext* ctx)    { return ctx ? ctx->channels    : 2;     }
CASTOR_CORE_API int file_capture_has_video(FileCaptureContext* ctx)   { return ctx && ctx->video_stream_idx >= 0; }
CASTOR_CORE_API int file_capture_has_audio(FileCaptureContext* ctx)   { return ctx && ctx->audio_stream_idx >= 0; }
