using Castor.Engine.Models;
using Castor.Native;

namespace Castor.Engine.Services;

public interface IRecorderService
{
    bool IsRecording { get; }
    bool IsStreaming { get; }

    event Action? RecordingStarted;
    event Action? RecordingStopped;
    event Action? StreamingStarted;
    event Action? StreamingStopped;

    bool IsPreviewActive(Guid sceneId);

    int Start(SceneItem scene, string outputPath,
              int fps = 30,
              int videoBitrateKbps = 0,
              CastorVideoCodec videoCodec = CastorVideoCodec.H264,
              CastorAudioCodec audioCodec = CastorAudioCodec.AAC,
              int outputWidth = 0, int outputHeight = 0,
              int qualityIndex = 1);

    void StopRecording();

    int StartStream(SceneItem scene, StreamingPlatform platform, string streamKeyOrUrl,
                    int fps = 30, int videoBitrateKbps = 4000);

    void StopStream();
    int StartPreview(SceneItem scene, int fps = 30);
    void StopPreview(SceneItem scene);
    void StopAllPreviews();
    void StopAll();
    int SwitchScene(SceneItem scene);
}
