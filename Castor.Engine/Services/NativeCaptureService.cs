using Castor.Engine.Models;
using Castor.Native;

namespace Castor.Engine.Services;

public sealed class NativeCaptureService : INativeCaptureService
{
    public void Initialize()
    {
        CastorNative.Initialize();
    }

    public IReadOnlyList<CaptureSourceOption> ListVideoSources()
    {
        return CastorNative.ListVideoSources()
            .Select(source => new CaptureSourceOption(source))
            .ToArray();
    }

    public IReadOnlyList<AudioSourceOption> ListAudioSources()
    {
        return CastorNative.ListAudioSources()
            .Select(source => new AudioSourceOption(source))
            .ToArray();
    }
}
