#include <libavutil/avutil.h>

#include "verison.h"

const char *get_version(void)
{
    const char *version = av_version_info();
    return version;
}