#pragma once
#include "castor_api.h"
#include <libavcodec/avcodec.h>
#include <libavutil/audio_fifo.h>
#include <libswresample/swresample.h>

/* Forward declaration — evite d'inclure Muxer.h et windows.h ici */
typedef struct CastorMuxer CastorMuxer;

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
} AudioEncoder;

/*
 * Initialise l'encodeur AAC (codec context, frame de travail, FIFO).
 * Ne cree pas de fichier de sortie — l'ecriture se fait via le muxer partage.
 *
 * enc         : encodeur a initialiser
 * sample_rate : frequence d'echantillonnage de la source (Hz)
 *
 * Retourne 0 si succes, -1 en cas d'erreur.
 */
CASTOR_CORE_API int  audio_encoder_init        (AudioEncoder* enc, int sample_rate);

/*
 * Encode un frame audio vers AAC.
 * Reechantillonne si le format/rate/canaux different de ceux de l'encodeur,
 * accumule les samples dans un FIFO, puis flush par blocs de frame_size (~1024 samples).
 * Ecrit les paquets dans le container MP4 via le muxer partage.
 *
 * enc : encodeur initialise
 * src : frame audio source (format, sample_rate et canaux quelconques)
 * mux : muxer MP4 partage (thread-safe)
 *
 * Retourne 0 si succes, -1 en cas d'erreur.
 */
CASTOR_CORE_API int  audio_encoder_encode_frame(AudioEncoder* enc, AVFrame* src, CastorMuxer* mux);

/*
 * Flush les samples residuels du FIFO, vide le pipeline de l'encodeur
 * et libere toutes les ressources.
 * Le trailer MP4 est ecrit par muxer_close — ne pas l'ecrire ici.
 *
 * enc : encodeur a nettoyer
 * mux : muxer MP4 partage (pour le flush final)
 */
CASTOR_CORE_API void audio_encoder_cleanup     (AudioEncoder* enc, CastorMuxer* mux);

#ifdef __cplusplus
}
#endif
