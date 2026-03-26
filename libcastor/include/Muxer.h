#pragma once
#include "castor_api.h"
#include <libavformat/avformat.h>
#include <libavcodec/avcodec.h>
#include <windows.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ------------------------------------------------------------------ *
 *  Muxer MP4 partage entre l'encodeur video et l'encodeur audio.
 *  Thread-safe : muxer_write_packet est protege par un CRITICAL_SECTION.
 * ------------------------------------------------------------------ */
typedef struct CastorMuxer {
    AVFormatContext* fmt_ctx;
    AVStream*        video_stream;
    AVStream*        audio_stream;
    CRITICAL_SECTION lock;
} CastorMuxer;

/* Cree le contexte de format MP4 et la section critique.
 * Doit etre appele apres video_encoder_init et audio_encoder_init.
 *
 * mux         : muxer a initialiser
 * output_path : chemin du fichier .mp4 de sortie
 *
 * Retourne 0 si succes, -1 en cas d'erreur. */
CASTOR_CORE_API int  muxer_open(CastorMuxer* mux, const char* output_path);

/* Ajoute le stream video au container MP4 a partir du codec context.
 * Doit etre appele apres muxer_open et avant muxer_write_header.
 *
 * mux  : muxer ouvert
 * vctx : codec context video initialise (H.264)
 *
 * Retourne 0 si succes, -1 en cas d'erreur. */
CASTOR_CORE_API int  muxer_add_video_stream(CastorMuxer* mux, AVCodecContext* vctx);

/* Ajoute le stream audio au container MP4 a partir du codec context.
 * Doit etre appele apres muxer_open et avant muxer_write_header.
 *
 * mux  : muxer ouvert
 * actx : codec context audio initialise (AAC)
 *
 * Retourne 0 si succes, -1 en cas d'erreur. */
CASTOR_CORE_API int  muxer_add_audio_stream(CastorMuxer* mux, AVCodecContext* actx);

/* Ouvre le fichier de sortie et ecrit l'en-tete MP4.
 * Doit etre appele apres muxer_add_video_stream et muxer_add_audio_stream.
 *
 * mux : muxer avec les deux streams ajoutes
 *
 * Retourne 0 si succes, -1 en cas d'erreur. */
CASTOR_CORE_API int  muxer_write_header(CastorMuxer* mux);

/* Ecrit un paquet encode dans le container MP4 (thread-safe).
 * Le paquet est consomme (unref) par av_interleaved_write_frame.
 *
 * mux : muxer pret
 * pkt : paquet a ecrire (stream_index et PTS/DTS doivent etre definis)
 *
 * Retourne 0 si succes, code d'erreur FFmpeg negatif sinon. */
CASTOR_CORE_API int  muxer_write_packet(CastorMuxer* mux, AVPacket* pkt);

/* Ecrit le trailer MP4, ferme le fichier et libere toutes les ressources.
 *
 * mux : muxer a fermer */
CASTOR_CORE_API void muxer_close(CastorMuxer* mux);

#ifdef __cplusplus
}
#endif
