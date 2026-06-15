#pragma once

#include "output.h"
#include "castor_api.h"

#ifdef __cplusplus
extern "C" {
#endif

/* ================================================================== *
 *  CastorFileOutput — sortie vers un fichier local (MP4, MKV, ...).
 *
 *  Le format est detecte automatiquement depuis l'extension du chemin.
 *  Exemple : "output.mp4" -> MP4, "output.mkv" -> Matroska.
 *
 *  Usage :
 *    CastorOutput* out = file_output_create("C:\\records\\stream.mp4");
 *    output_add_video_stream(out, vctx);
 *    output_add_audio_stream(out, actx);
 *    output_write_header(out);
 *    // ... output_write_packet(out, pkt) ...
 *    output_close(out);
 *    output_destroy(&out);
 * ================================================================== */

CASTOR_CORE_API CastorOutput* file_output_create(const char* path);

#ifdef __cplusplus
}
#endif
