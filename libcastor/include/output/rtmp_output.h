#pragma once

#include "output.h"
#include "castor_api.h"

#ifdef __cplusplus
extern "C" {
#endif

/* ================================================================== *
 *  RtmpOutputConfig — parametres de la sortie RTMP.
 *
 *  Valeurs recommandees selon la plateforme :
 *
 *  Twitch  : video_bitrate_kbps = 6000, audio_bitrate_kbps = 160, gop_seconds = 2
 *  YouTube : video_bitrate_kbps = 4500, audio_bitrate_kbps = 128, gop_seconds = 2
 *
 *  Mettre une valeur a 0 applique le defaut interne :
 *    video_bitrate_kbps = 4000
 *    audio_bitrate_kbps = 128
 *    gop_seconds        = 2
 * ================================================================== */
typedef struct {
    char url[512];             /* rtmp://host/app/streamkey ou rtmps://... */
    int  video_bitrate_kbps;   /* debit video cible en kb/s (CBR)          */
    int  audio_bitrate_kbps;   /* debit audio cible en kb/s                */
    int  gop_seconds;          /* intervalle entre keyframes en secondes   */
} RtmpOutputConfig;

/* ================================================================== *
 *  CastorRtmpOutput — sortie vers un serveur RTMP/RTMPS.
 *
 *  Container : FLV (requis par le protocole RTMP).
 *  Video     : H.264, CBR, tune=zerolatency.
 *  Audio     : AAC.
 *
 *  Note : les parametres CBR et zerolatency sont configures dans
 *  l'encodeur video (VideoEncoderConfig), pas ici. Cet output se
 *  contente de gerer la connexion et l'ecriture des paquets.
 *
 *  Usage :
 *    RtmpOutputConfig cfg = {
 *        .url                 = "rtmp://live.twitch.tv/live/sk_...",
 *        .video_bitrate_kbps  = 6000,
 *        .audio_bitrate_kbps  = 160,
 *        .gop_seconds         = 2,
 *    };
 *    CastorOutput* out = rtmp_output_create(&cfg);
 *    output_add_video_stream(out, vctx);
 *    output_add_audio_stream(out, actx);
 *    output_write_header(out);   // ouvre la connexion RTMP
 *    // ... output_write_packet(out, pkt) ...
 *    output_close(out);
 *    output_destroy(&out);
 * ================================================================== */

CASTOR_CORE_API CastorOutput* rtmp_output_create(const RtmpOutputConfig* config);

#ifdef __cplusplus
}
#endif
