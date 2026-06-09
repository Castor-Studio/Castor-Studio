using System.Collections.ObjectModel;
using Castor.Engine.Models;
using Castor.Native;

namespace Castor.Engine.Services;

public sealed class StudioController(
    ISceneService sceneService,
    IRecorderService recorderService,
    IMediaMtxService mediaMtxService) : IStudioController
{
    public ObservableCollection<SceneItem> Scenes => sceneService.Scenes;
    public SceneItem? ActiveScene => sceneService.ActiveScene;
    public bool IsRecording => recorderService.IsRecording;
    public bool IsStreaming => recorderService.IsStreaming;

    public event Action? RecordingStarted
    {
        add => recorderService.RecordingStarted += value;
        remove => recorderService.RecordingStarted -= value;
    }

    public event Action? RecordingStopped
    {
        add => recorderService.RecordingStopped += value;
        remove => recorderService.RecordingStopped -= value;
    }

    public event Action? StreamingStarted
    {
        add => recorderService.StreamingStarted += value;
        remove => recorderService.StreamingStarted -= value;
    }

    public event Action? StreamingStopped
    {
        add => recorderService.StreamingStopped += value;
        remove => recorderService.StreamingStopped -= value;
    }

    public SceneItem CreateScene(string name) => sceneService.CreateScene(name);

    public void DeleteScene(SceneItem scene) => sceneService.DeleteScene(scene);

    public void SelectScene(SceneItem scene)
    {
        sceneService.SetActiveScene(scene);

        if (recorderService.IsRecording || recorderService.IsStreaming)
            _ = Task.Run(() => recorderService.SwitchScene(scene));

        if (HasVideoSource(scene) && !recorderService.IsPreviewActive(scene.Id))
            _ = Task.Run(() => recorderService.StartPreview(scene));
    }

    public SourceItem AddVideoSource(SceneItem scene, CaptureSourceOption source)
    {
        return sceneService.AddVideoSource(scene, source);
    }

    public SourceItem AddNetworkVideoSource(SceneItem scene, string label, string url)
    {
        return sceneService.AddVideoSource(scene, label, url);
    }

    public SourceItem AddAudioSource(SceneItem scene, AudioSourceOption source)
    {
        return sceneService.AddAudioSource(scene, source);
    }

    public void RemoveSource(SceneItem scene, SourceItem source)
    {
        sceneService.RemoveSource(scene, source);
    }

    public bool HasVideoSource(SceneItem scene)
    {
        return scene.Sources.Any(source => source.Kind == SourceKind.Video);
    }

    public bool IsPreviewActive(Guid sceneId) => recorderService.IsPreviewActive(sceneId);

    public int EnsurePreview(SceneItem scene)
    {
        if (!HasVideoSource(scene)) return -2;
        if (recorderService.IsPreviewActive(scene.Id)) return 0;
        return recorderService.StartPreview(scene);
    }

    public string GetPreviewPullUrl(Guid sceneId) => mediaMtxService.GetPreviewPullUrl(sceneId);

    public int StartRecording(SceneItem scene, string outputPath,
                              int fps = 30,
                              int videoBitrateKbps = 0,
                              CastorVideoCodec videoCodec = CastorVideoCodec.H264,
                              CastorAudioCodec audioCodec = CastorAudioCodec.AAC,
                              int outputWidth = 0, int outputHeight = 0,
                              int qualityIndex = 1)
    {
        return recorderService.Start(scene, outputPath, fps, videoBitrateKbps,
                                     videoCodec, audioCodec, outputWidth, outputHeight, qualityIndex);
    }

    public void StopRecording()
    {
        recorderService.StopRecording();
    }

    public int StartStream(SceneItem scene, StreamingPlatform platform, string streamKeyOrUrl,
                           int fps = 30, int videoBitrateKbps = 4000)
    {
        return recorderService.StartStream(scene, platform, streamKeyOrUrl, fps, videoBitrateKbps);
    }

    public void StopStream()
    {
        recorderService.StopStream();
    }
}
