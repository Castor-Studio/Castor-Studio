using System.Collections.ObjectModel;
using Castor.Engine.Models;
using Castor.Native;

namespace Castor.Engine.Services;

public interface IStudioController
{
    ObservableCollection<SceneItem> Scenes { get; }
    SceneItem? ActiveScene { get; }
    bool IsRecording { get; }
    bool IsStreaming { get; }

    event Action? RecordingStarted;
    event Action? RecordingStopped;
    event Action? StreamingStarted;
    event Action? StreamingStopped;
    event Action? ActiveSceneChanged;

    SceneItem CreateScene(string name);
    void DeleteScene(SceneItem scene);
    void RenameScene(SceneItem scene, string newName);
    void SelectScene(SceneItem scene);
    SourceItem AddVideoSource(SceneItem scene, CaptureSourceOption source);
    SourceItem AddNetworkVideoSource(SceneItem scene, string label, string url);
    SourceItem AddAudioSource(SceneItem scene, AudioSourceOption source);
    SourceItem AddFileVideoSource(SceneItem scene, FileVideoSourceOption option);
    SourceItem AddFileAudioSource(SceneItem scene, FileAudioSourceOption option);
    void RemoveSource(SceneItem scene, SourceItem source);
    bool HasVideoSource(SceneItem scene);
    bool IsPreviewActive(Guid sceneId);
    int EnsurePreview(SceneItem scene);
    void RestartPreview(SceneItem scene);
    string GetPreviewPullUrl(Guid sceneId);
    int StartRecording(SceneItem scene, string outputPath,
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
}
