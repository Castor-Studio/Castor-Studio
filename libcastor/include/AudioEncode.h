#pragma once
#include "castor_api.h"
#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
#include <libavutil/audio_fifo.h>
#include <libswresample/swresample.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct {
    AVCodecContext*  ctx;
    AVFrame*         frame;
    AVPacket*        pkt;
    AVAudioFifo*     fifo;
    SwrContext*      swr;
    int64_t          sample_index;
    AVFormatContext* fmt_ctx;
    AVStream*        stream;
} AudioEncoder;

/*
 * Initialise l'encodeur AAC et ouvre le fichier de sortie avec le muxer ADTS.
 * Chaque paquet encodé sera précédé d'un en-tête ADTS (7 octets),
 * ce qui rend le fichier .aac directement lisible par les lecteurs.
 *
 * enc         : encodeur à initialiser
 * sample_rate : fréquence d'échantillonnage de la source (Hz)
 * output_path : chemin du fichier .aac à créer
 *
 * Retourne 0 si succès, -1 en cas d'erreur.
 */
CASTOR_CORE_API int  audio_encoder_init        (AudioEncoder* enc, int sample_rate, const char* output_path);

/*
 * Encode un frame audio vers AAC.
 * Rééchantillonne si le format/rate/canaux diffèrent de ceux de l'encodeur,
 * accumule les samples dans un FIFO, puis flush par blocs de frame_size (~1024 samples).
 *
 * enc : encodeur initialisé
 * src : frame audio source (format, sample_rate et canaux quelconques)
 *
 * Retourne 0 si succès, -1 en cas d'erreur.
 */
CASTOR_CORE_API int  audio_encoder_encode_frame(AudioEncoder* enc, AVFrame* src);

/*
 * Flush les samples résiduels du FIFO, vide le pipeline de l'encodeur,
 * écrit le trailer ADTS, ferme le fichier et libère toutes les ressources.
 *
 * enc : encodeur à nettoyer
 */
CASTOR_CORE_API void audio_encoder_cleanup     (AudioEncoder* enc);

#ifdef __cplusplus
}
#endif
