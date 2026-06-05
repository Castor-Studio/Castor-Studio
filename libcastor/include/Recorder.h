#pragma once
#include "castor_api.h"
#include "VideoCapture.h"
#include "AudioCapture.h"
#include "VideoEncode.h"
#include "AudioEncode.h"

#ifdef __cplusplus
extern "C" {
#endif

#define CASTOR_MAX_STREAMS 8

/* ================================================================== *
 *  OutputConfig — destination et parametres d'encodage pour un stream.
 *
 *  type = CASTOR_OUTPUT_FILE  ->  destination = chemin fichier (.mp4/.mkv/.webm)
 *  type = CASTOR_OUTPUT_RTMP  ->  destination = URL RTMP/RTMPS
 *
 *  video_bitrate_kbps / audio_bitrate_kbps : 0 = qualite par defaut (CRF/CQ).
 *  Si > 0, encodage CBR — recommande pour RTMP, optionnel pour fichier.
 *  gop_seconds : 0 = defaut (2s).
 *  video_codec / audio_codec : RTMP force toujours H264+AAC.
 *
 *  Terrain pour sorties multiples (record + stream simultanement) :
 *  StreamConfig passera de "OutputConfig output" a
 *  "OutputConfig outputs[CASTOR_MAX_OUTPUTS]; int num_outputs"
 *  quand la fonctionnalite sera implementee.
 * ================================================================== */
typedef enum {
    CASTOR_OUTPUT_FILE = 0,  /* fichier local (MP4, MKV, ...) */
    CASTOR_OUTPUT_RTMP = 1,  /* serveur RTMP / RTMPS          */
} CastorOutputType;

typedef struct {
    CastorOutputType type;
    char             destination[512];    /* chemin fichier ou rtmp:// URL  */
    int              video_bitrate_kbps;  /* 0 = defaut (4000 en RTMP)      */
    int              audio_bitrate_kbps;  /* 0 = defaut (128 en RTMP)       */
    int              gop_seconds;         /* 0 = defaut (2s)                */
    CastorVideoCodec video_codec;         /* codec video (H264 par defaut)  */
    CastorAudioCodec audio_codec;         /* codec audio (AAC par defaut)   */
    int              output_width;        /* 0 = meme que la capture        */
    int              output_height;       /* 0 = meme que la capture        */
    int              quality_index;       /* 0=haute 1=bonne 2=basse        */
} OutputConfig;

/* ================================================================== *
 *  StreamConfig — configuration complete d'un stream
 * ================================================================== */
typedef struct {
    CaptureSourceInfo  video_src;
    AudioSourceInfo    audio_src;
    OutputConfig       output;
} StreamConfig;

/* ================================================================== *
 *  RecorderConfig — configuration globale du recorder
 * ================================================================== */
typedef struct {
    StreamConfig streams[CASTOR_MAX_STREAMS];
    int          num_streams;
    int          fps;
} RecorderConfig;

typedef struct CastorRecorder CastorRecorder;

CASTOR_CORE_API CastorRecorder* recorder_create (const RecorderConfig* config);
CASTOR_CORE_API int             recorder_start  (CastorRecorder* rec);
CASTOR_CORE_API void            recorder_stop   (CastorRecorder* rec);
CASTOR_CORE_API void            recorder_destroy(CastorRecorder* rec);

CASTOR_CORE_API int recorder_switch_video_source(CastorRecorder*          rec,
                                                  int                      stream_index,
                                                  const CaptureSourceInfo* new_src);

#ifdef __cplusplus
}
#endif
