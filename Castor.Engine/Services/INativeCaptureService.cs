using Castor.Engine.Models;

namespace Castor.Engine.Services;

public interface INativeCaptureService
{
    void Initialize();
    IReadOnlyList<CaptureSourceOption> ListVideoSources();
    IReadOnlyList<AudioSourceOption> ListAudioSources();
}
