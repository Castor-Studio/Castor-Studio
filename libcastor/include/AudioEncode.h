#pragma once
#include "castor_api.h"
#include <libavcodec/avcodec.h>
#include <libavutil/audio_fifo.h>
#include <libswresample/swresample.h>

/* Forward declarations */
typedef struct CastorMuxer  CastorMuxer;
typedef struct CastorOutput CastorOutput;

#ifdef __cplusplus
extern "C" {
#endif

/* ================================================================== *
 *  AudioEncoderConfig — parametres d'encodage audio.
 *
 *  audio_bitrate_kbps : 0 = defaut (128 kb/s)
 * ================================================================== */
typedef struct {
    int audio_bitrate_kbps;
} AudioEncoderConfig;

static inline AudioEncoderConfig audio_encoder_config_default(void)
{
    AudioEncoderConfig c;
    c.audio_bitrate_kbps = 128;
    return c;
}

/* ================================================================== *
 *  AudioEncoder
 * ================================================================== */
typedef struct {
    AVCodecContext*  ctx;
    AVFrame*         frame;
    AVPacket*        pkt;
    AVAudioFifo*     fifo;
    SwrContext*      swr;
    int64_t          sample_index;
} AudioEncoder;

/*
 * Initialise l'encodeur AAC avec la configuration par defaut (128 kb/s).
 */
CASTOR_CORE_API int  audio_encoder_init   (AudioEncoder* enc, int sample_rate);

/*
 * Initialise l'encodeur AAC avec une configuration complete.
 *
 * enc         : encodeur a initialiser
 * sample_rate : frequence d'echantillonnage de la source (Hz)
 * cfg         : parametres d'encodage (NULL = defaut)
 *
 * Retourne 0 si succes, -1 en cas d'erreur.
 */
CASTOR_CORE_API int  audio_encoder_init_ex(AudioEncoder* enc, int sample_rate,
                                           const AudioEncoderConfig* cfg);

/*
 * Encode un frame audio vers AAC et ecrit les paquets via CastorOutput.
 */
CASTOR_CORE_API int  audio_encoder_encode_frame(AudioEncoder* enc, AVFrame* src, CastorOutput* out);

/*
 * Flush les samples residuels et libere toutes les ressources.
 */
CASTOR_CORE_API void audio_encoder_cleanup     (AudioEncoder* enc, CastorOutput* out);

#ifdef __cplusplus
}
#endif
