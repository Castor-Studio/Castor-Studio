#include "VideoEncode.h"
#include "output/output.h"
#include <libavutil/imgutils.h>
#include <libavutil/opt.h>
#include <libswscale/swscale.h>
#include <stdio.h>

/* ------------------------------------------------------------------ *
 *  video_encoder_init_ex — implementation centrale
 * ------------------------------------------------------------------ */
CASTOR_CORE_API int video_encoder_init_ex(VideoEncoder* enc, int width, int height, int fps,
                                          const VideoEncoderConfig* cfg)
{
    VideoEncoderConfig defaults = video_encoder_config_default();
    if (!cfg) cfg = &defaults;

    const AVCodec* codec = NULL;
    const int use_vp9 = (cfg->video_codec == CASTOR_VCODEC_VP9);

    /* Codes de retour :
     *  -20 : codec introuvable (libvpx-vp9 / libx264 absent de l'avcodec.dll)
     *  -21 : avcodec_open2 a echoue (configuration invalide)
     *  -22 : sws_getContext a echoue */
    if (use_vp9) {
        codec = avcodec_find_encoder_by_name("libvpx-vp9");
        if (!codec) {
            fprintf(stderr, "[VideoEncoder] libvpx-vp9 introuvable — recompilez FFmpeg avec --enable-libvpx\n");
            return -20;
        }
    } else {
        /* Preferer libx264 : supporte CBR, preset, tune=zerolatency.
         * h264_mf (Media Foundation) ignore ces options et est deconseille pour RTMP. */
        codec = avcodec_find_encoder_by_name("libx264");
        if (!codec) {
            fprintf(stderr, "[VideoEncoder] libx264 introuvable, fallback vers codec H264 systeme\n");
            codec = avcodec_find_encoder(AV_CODEC_ID_H264);
        }
        if (!codec) {
            fprintf(stderr, "[VideoEncoder] aucun codec H264 disponible\n");
            return -20;
        }
    }
    fprintf(stderr, "[VideoEncoder] codec : %s\n", codec->name);

    enc->ctx = avcodec_alloc_context3(codec);
    if (!enc->ctx) return -1;

    /* gop_seconds == 0 :
     *   - zerolatency (preview) → fps/2 frames = 0,5 s à 30 fps : IDR fréquents,
     *     permet à VLC de rejoindre le flux rapidement après une reconnexion.
     *   - mode normal           → 2 s (défaut standard pour le broadcast). */
    const int gop = cfg->gop_seconds > 0 ? cfg->gop_seconds * fps
                  : (cfg->zerolatency   ? fps / 2 : 2 * fps);

    enc->ctx->width        = width;
    enc->ctx->height       = height;
    enc->ctx->time_base    = (AVRational){ 1, fps };
    enc->ctx->framerate    = (AVRational){ fps, 1 };
    enc->ctx->pix_fmt      = AV_PIX_FMT_YUV420P;
    enc->ctx->gop_size     = gop;
    enc->ctx->max_b_frames = 0;
    /* AV_CODEC_FLAG_GLOBAL_HEADER :
     *   H.264 en a besoin (SPS/PPS dans l'avcC/hvcC de MP4/MKV et dans le
     *   RTMP AVCDecoderConfigurationRecord).
     *   VP9 n'en a pas besoin : le bitstream est auto-suffisant, et mettre ce
     *   flag peut produire un CodecPrivate incompatible avec certains players. */
    if (!use_vp9)
        enc->ctx->flags |= AV_CODEC_FLAG_GLOBAL_HEADER;

    enc->first_pts     = 0;
    enc->first_pts_set = 0;

    if (use_vp9) {
        /* --- libvpx-vp9 ---
         *
         * lag-in-frames=0 : desactive le lookahead interne de VP9.
         * Sans ca, meme en mode CBR, deadline=good active un lookahead de
         * plusieurs frames : les paquets video ne sortent qu'au flush final,
         * et av_interleaved_write_frame ne peut plus entrelacer audio+video
         * correctement → image figee a la lecture.
         * 0 = pas de lookahead, sortie paquet-par-paquet comme libx264. */
        av_opt_set_int(enc->ctx->priv_data, "lag-in-frames", 0, 0);

        if (cfg->cbr && cfg->video_bitrate_kbps > 0) {
            const int bps = cfg->video_bitrate_kbps * 1000;
            enc->ctx->bit_rate       = bps;
            enc->ctx->rc_min_rate    = bps;
            enc->ctx->rc_max_rate    = bps;
            enc->ctx->rc_buffer_size = cfg->zerolatency ? bps / 2 : bps * 2;
            /* end-usage=cbr est obligatoire : sans lui, libvpx ignore bit_rate
             * et rc_min/max_rate, ce qui peut faire echouer avcodec_open2.
             *
             * deadline=realtime (pas "good") : en mode "good", libvpx peut
             * prendre >16 ms par frame a 1080p, ce qui depasse le frame_interval
             * a 60fps. Le buffer de av_interleaved_write_frame sature alors en
             * ~0,5 s et les paquets VP9 ne sont plus ecrits → image figee.
             * "realtime" garantit un paquet sorti pour chaque frame envoyee,
             * quel que soit le debit CPU disponible.
             * cpu-used : 8=preview ultra-rapide, 6=enregistrement fichier. */
            av_opt_set(enc->ctx->priv_data, "end-usage", "cbr", 0);
            av_opt_set(enc->ctx->priv_data, "deadline", "realtime", 0);
            av_opt_set(enc->ctx->priv_data, "cpu-used",
                       cfg->zerolatency ? "8" : "6", 0);
        } else {
            /* CQ (constrained quality) — equivalent CRF pour VP9.
             * Meme contrainte temps-reel : deadline=realtime + cpu-used=8. */
            enc->ctx->bit_rate = 0;
            av_opt_set(enc->ctx->priv_data, "end-usage", "q", 0);
            av_opt_set(enc->ctx->priv_data, "deadline", "realtime", 0);
            /* cpu-used=8 : encodage le plus rapide (qualite legèrement reduite).
             * VP9 en mode fichier peut etre lent a haute resolution ; 8 permet
             * d'approcher le fps cible sans ralentir le thread d'encodage. */
            av_opt_set(enc->ctx->priv_data, "cpu-used", "8", 0);
            av_opt_set_int(enc->ctx->priv_data, "crf",
                           cfg->crf > 0 ? cfg->crf : 33,
                           AV_OPT_SEARCH_CHILDREN);
        }
    } else {
        /* --- libx264 --- */

        /* Preset */
        av_opt_set(enc->ctx->priv_data, "preset",
                   cfg->zerolatency ? "ultrafast" : "veryfast", 0);

        /* Latence zero (streaming) */
        if (cfg->zerolatency)
            av_opt_set(enc->ctx->priv_data, "tune", "zerolatency", 0);

        /* Mode CBR ou CRF */
        if (cfg->cbr && cfg->video_bitrate_kbps > 0) {
            const int bps = cfg->video_bitrate_kbps * 1000;
            enc->ctx->bit_rate       = bps;
            enc->ctx->rc_min_rate    = bps;
            enc->ctx->rc_max_rate    = bps;
            /* En mode zerolatency (preview live) : VBV = 0.5 s → réduit le buffer
             * encoder côté RTMP sans casser le flux.
             * En mode normal (broadcast) : VBV = 2 s → headroom réseau standard. */
            enc->ctx->rc_buffer_size = cfg->zerolatency ? bps / 2 : bps * 2;
        } else if (cfg->crf > 0) {
            /* CRF explicite fourni par l'utilisateur (qualite configurable). */
            av_opt_set_int(enc->ctx->priv_data, "crf", cfg->crf, AV_OPT_SEARCH_CHILDREN);
        }
    }

    if (avcodec_open2(enc->ctx, codec, NULL) < 0) {
        fprintf(stderr, "[VideoEncoder] avcodec_open2 failed\n");
        avcodec_free_context(&enc->ctx);
        return -21;
    }

    /* Certains encodeurs (libvpx-vp9) modifient ctx->time_base apres open.
     * On sauvegarde le fps cible pour calculer correctement les pts via
     * av_rescale_q dans video_encoder_encode_frame. */
    enc->fps = fps;
    fprintf(stderr, "[VideoEncoder] time_base apres open : %d/%d (fps cible : %d)\n",
            enc->ctx->time_base.num, enc->ctx->time_base.den, fps);

    /* Force time_base et framerate apres open : libvpx-vp9 peut les modifier.
     * Avec lag-in-frames=0, VP9 repasse les pts 1:1 (input→output),
     * donc forcer ces valeurs est sans risque et garantit la coherence
     * avec muxer_add_video_stream et av_packet_rescale_ts. */
    enc->ctx->time_base = (AVRational){ 1, fps };
    enc->ctx->framerate = (AVRational){ fps, 1 };
    fprintf(stderr, "[VideoEncoder] time_base FORCE: %d/%d  framerate FORCE: %d/%d\n",
            enc->ctx->time_base.num, enc->ctx->time_base.den,
            enc->ctx->framerate.num, enc->ctx->framerate.den);

    enc->frame = av_frame_alloc();
    enc->frame->format = AV_PIX_FMT_YUV420P;
    enc->frame->width  = width;
    enc->frame->height = height;
    av_frame_get_buffer(enc->frame, 32);

    /* src_width/src_height : dimensions de la frame capturee (entree sws).
     * width/height          : dimensions de sortie (encodeur + frame allouee).
     * Si src non specifie, on suppose capture = sortie (pas de redimensionnement). */
    const int sws_src_w = (cfg->src_width  > 0) ? cfg->src_width  : width;
    const int sws_src_h = (cfg->src_height > 0) ? cfg->src_height : height;

    enc->sws_ctx = sws_getContext(
        sws_src_w, sws_src_h, AV_PIX_FMT_BGRA,
        width,     height,    AV_PIX_FMT_YUV420P,
        SWS_BILINEAR, NULL, NULL, NULL
    );
    if (!enc->sws_ctx) {
        fprintf(stderr, "[VideoEncoder] sws_getContext failed\n");
        avcodec_free_context(&enc->ctx);
        av_frame_free(&enc->frame);
        return -22;
    }

    enc->pkt         = av_packet_alloc();
    enc->frame_index = 0;
    return 0;
}

/* Compatibilite — utilise la config par defaut (CRF) */
CASTOR_CORE_API int video_encoder_init(VideoEncoder* enc, int width, int height, int fps)
{
    return video_encoder_init_ex(enc, width, height, fps, NULL);
}

/* ------------------------------------------------------------------ *
 *  Flush interne
 * ------------------------------------------------------------------ */
static int flush_encoder(VideoEncoder* enc, CastorOutput* out)
{
    if (avcodec_send_frame(enc->ctx, NULL) < 0) return -1;
    while (avcodec_receive_packet(enc->ctx, enc->pkt) == 0) {
        enc->pkt->stream_index = out->video_stream_index;
        av_packet_rescale_ts(enc->pkt, enc->ctx->time_base,
                             out->video_stream_time_base);
        output_write_packet(out, enc->pkt);
        av_packet_unref(enc->pkt);
    }
    return 0;
}

/* ------------------------------------------------------------------ *
 *  video_encoder_cleanup
 * ------------------------------------------------------------------ */
CASTOR_CORE_API void video_encoder_cleanup(VideoEncoder* enc, CastorOutput* out)
{
    flush_encoder(enc, out);
    sws_freeContext(enc->sws_ctx);
    avcodec_free_context(&enc->ctx);
    av_frame_free(&enc->frame);
    av_packet_free(&enc->pkt);
}

/* ------------------------------------------------------------------ *
 *  video_encoder_encode_frame
 * ------------------------------------------------------------------ */
CASTOR_CORE_API int video_encoder_encode_frame(VideoEncoder* enc, AVFrame* src, CastorOutput* out)
{
    av_frame_make_writable(enc->frame);

    sws_scale(
        enc->sws_ctx,
        (const uint8_t* const*)src->data, src->linesize,
        0, src->height,
        enc->frame->data, enc->frame->linesize
    );

    if (!enc->first_pts_set) {
        enc->first_pts     = src->pts;
        enc->first_pts_set = 1;
    }

    /* src->pts est l'horloge murale en microsecondes depuis le debut de
     * l'enregistrement, definie par thread_stream_video_encode.
     * Cela garantit une vitesse de lecture 1:1 meme si l'encodeur est plus
     * lent que le fps cible (ex : VP9 a 11fps effectif sur une cible de 60fps).
     * Fallback sur frame_index si src->pts n'est pas disponible. */
    if (src->pts != AV_NOPTS_VALUE && src->pts >= 0) {
        enc->frame->pts = av_rescale_q(src->pts,
                                       (AVRational){ 1, 1000000 },
                                       enc->ctx->time_base);
    } else {
        enc->frame->pts = av_rescale_q(enc->frame_index,
                                       (AVRational){ 1, enc->fps },
                                       enc->ctx->time_base);
    }

    enc->frame_index++;

    int ret = avcodec_send_frame(enc->ctx, enc->frame);
    if (ret < 0) {
        char errbuf[64];
        av_strerror(ret, errbuf, sizeof(errbuf));
        fprintf(stderr, "[VideoEncoder] send_frame: %s\n", errbuf);
        return ret;
    }

    while (avcodec_receive_packet(enc->ctx, enc->pkt) == 0) {
        enc->pkt->stream_index = out->video_stream_index;

        if (enc->frame_index <= 5) {
            fprintf(stderr, "[VideoEnc DBG] pkt PRE-rescale : pts=%lld dts=%lld"
                            "  src_tb=%d/%d  dst_tb=%d/%d\n",
                    (long long)enc->pkt->pts, (long long)enc->pkt->dts,
                    enc->ctx->time_base.num, enc->ctx->time_base.den,
                    out->video_stream_time_base.num, out->video_stream_time_base.den);
        }

        av_packet_rescale_ts(enc->pkt, enc->ctx->time_base,
                             out->video_stream_time_base);

        if (enc->frame_index <= 5) {
            fprintf(stderr, "[VideoEnc DBG] pkt POST-rescale: pts=%lld dts=%lld\n",
                    (long long)enc->pkt->pts, (long long)enc->pkt->dts);
        }

        output_write_packet(out, enc->pkt);
        av_packet_unref(enc->pkt);
    }
    return 0;
}
