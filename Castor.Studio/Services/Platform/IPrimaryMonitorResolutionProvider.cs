using CastorApplication.Models.Settings;

namespace CastorApplication.Services.Platform;

internal interface IPrimaryMonitorResolutionProvider
{
    VideoCanvasResolution GetPrimaryMonitorResolution();
}
