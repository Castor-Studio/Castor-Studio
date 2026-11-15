using CastorApplication.Models.Settings;
using CastorApplication.Services.Platform;

namespace Castor.Studio.Tests;

internal sealed class TestResolutionProvider(VideoCanvasResolution resolution)
    : IPrimaryMonitorResolutionProvider
{
    public VideoCanvasResolution GetPrimaryMonitorResolution() => resolution;
}
