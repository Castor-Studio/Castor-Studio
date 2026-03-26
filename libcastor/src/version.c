#include <libavutil/avutil.h>

#include "version.h"

const char *get_version(void)
{
    const char *version = av_version_info();
    return version;
}