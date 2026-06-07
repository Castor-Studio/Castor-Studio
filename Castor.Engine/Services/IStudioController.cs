using System.Collections.ObjectModel;
using Castor.Engine.Models;

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

    SceneItem CreateScene(string name);
    void DeleteScene(SceneItem scene);
    void SelectScene(SceneItem scene);
    SourceItem AddVideoSource(SceneItem scene, CaptureSourceOption source);
    SourceItem AddNetworkVideoSource(SceneItem scene, string label, string url);
    SourceItem AddAudioSource(SceneItem scene, AudioSourceOption source);
    void RemoveSource(SceneItem scene, SourceItem source);
    bool HasVideoSource(SceneItem scene);
    bool IsPreviewActive(Guid sceneId);
    int EnsurePreview(SceneItem scene);
    string GetPreviewPullUrl(Guid sceneId);
    int StartRecording(SceneItem scene, string outputPath);
    void StopRecording();
    int StartStream(SceneItem scene, StreamingPlatform platform, string streamKeyOrUrl);
    void StopStream();
}
