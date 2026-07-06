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

    public event Action? ActiveSceneChanged;

    public SceneItem CreateScene(string name)
    {
        var previousScene = sceneService.ActiveScene;
        var scene = sceneService.CreateScene(name);
        NotifyActiveSceneChangedIfNeeded(previousScene);
        return scene;
    }

    public void DeleteScene(SceneItem scene)
    {
        var previousScene = sceneService.ActiveScene;

        if (recorderService.IsPreviewActive(scene.Id))
            recorderService.StopPreview(scene);

        sceneService.DeleteScene(scene);
        NotifyActiveSceneChangedIfNeeded(previousScene);
    }

    public void RenameScene(SceneItem scene, string newName) => sceneService.RenameScene(scene, newName);

    public void SelectScene(SceneItem scene)
    {
        var previousScene = sceneService.ActiveScene;
        sceneService.SetActiveScene(scene);
        NotifyActiveSceneChangedIfNeeded(previousScene);

        if (recorderService.IsRecording || recorderService.IsStreaming)
            _ = Task.Run(() => recorderService.SwitchScene(scene));

        if (HasVideoSource(scene) && !recorderService.IsPreviewActive(scene.Id))
            _ = Task.Run(() => recorderService.StartPreview(scene));
    }

    private void NotifyActiveSceneChangedIfNeeded(SceneItem? previousScene)
    {
        if (previousScene == sceneService.ActiveScene) return;
        ActiveSceneChanged?.Invoke();
    }

    public SourceItem AddVideoSource(SceneItem scene, CaptureSourceOption source)
    {
        return ReplaceSourcesAndRestartPreview(
            scene,
            SourceKind.Video,
            () => sceneService.AddVideoSource(scene, source));
    }

    public SourceItem AddNetworkVideoSource(SceneItem scene, string label, string url)
    {
        return ReplaceSourcesAndRestartPreview(
            scene,
            SourceKind.Video,
            () => sceneService.AddVideoSource(scene, label, url));
    }

    public SourceItem AddAudioSource(SceneItem scene, AudioSourceOption source)
    {
        return ReplaceSourcesAndRestartPreview(
            scene,
            SourceKind.Audio,
            () => sceneService.AddAudioSource(scene, source));
    }

    public SourceItem AddFileVideoSource(SceneItem scene, FileVideoSourceOption option)
    {
        return ReplaceSourcesAndRestartPreview(
            scene,
            SourceKind.Video,
            () => sceneService.AddFileVideoSource(scene, option));
    }

    public SourceItem AddFileAudioSource(SceneItem scene, FileAudioSourceOption option)
    {
        return ReplaceSourcesAndRestartPreview(
            scene,
            SourceKind.Audio,
            () => sceneService.AddFileAudioSource(scene, option));
    }

    public void RemoveSource(SceneItem scene, SourceItem source)
    {
        var previewWasActive = recorderService.IsPreviewActive(scene.Id);
        if (previewWasActive)
            recorderService.StopPreview(scene);

        sceneService.RemoveSource(scene, source);

        RestartPreviewIfNeeded(scene, previewWasActive);
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

    /// <summary>
    /// Force la (re)création de la preview d'une scène, même si elle est déjà active.
    /// Utilisé pour démarrer plusieurs scènes à sources fichier au même instant : comme
    /// elles partagent la même horloge de synchro native (voir castor_file_sync_epoch_us),
    /// les relancer coup sur coup les garde en phase les unes avec les autres.
    /// </summary>
    public void RestartPreview(SceneItem scene)
    {
        if (recorderService.IsPreviewActive(scene.Id))
            recorderService.StopPreview(scene);

        if (HasVideoSource(scene))
            _ = Task.Run(() => recorderService.StartPreview(scene));
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

    private SourceItem ReplaceSourcesAndRestartPreview(
        SceneItem scene,
        SourceKind kind,
        Func<SourceItem> addSource)
    {
        var previewWasActive = recorderService.IsPreviewActive(scene.Id);
        if (previewWasActive)
            recorderService.StopPreview(scene);

        foreach (var existing in scene.Sources.Where(source => source.Kind == kind).ToArray())
            sceneService.RemoveSource(scene, existing);

        var item = addSource();

        RestartPreviewIfNeeded(scene, previewWasActive);

        if ((recorderService.IsRecording || recorderService.IsStreaming) && scene == sceneService.ActiveScene)
            _ = Task.Run(() => recorderService.SwitchScene(scene));

        return item;
    }

    private void RestartPreviewIfNeeded(SceneItem scene, bool previewWasActive)
    {
        if (!previewWasActive || !HasVideoSource(scene)) return;
        _ = Task.Run(() => recorderService.StartPreview(scene));
    }
}
