#include "utils/sync_clock.h"

#include <windows.h>
#include <libavutil/time.h>

static volatile LONGLONG s_epoch_us = 0;

CASTOR_CORE_API int64_t castor_file_sync_epoch_us(void) {
    LONGLONG now = av_gettime_relative();
    /* CAS : si personne n'a encore pose l'epoch (0), on tente de le poser.
     * En cas de course entre deux sources ouvertes au meme instant, un seul
     * thread "gagne" — l'ecart de quelques microsecondes entre candidats est
     * negligeable au regard de la duree d'une boucle video. */
    InterlockedCompareExchange64(&s_epoch_us, now, 0);
    return s_epoch_us;
}
