using System.Collections.ObjectModel;
using Castor.Engine.Models;
using Castor.Native;

namespace Castor.Engine.Services;

public sealed class SceneService : ISceneService
{
    public ObservableCollection<SceneItem> Scenes { get; } = new();

    private SceneItem? _activeScene;

    public SceneItem? ActiveScene
    {
        get => _activeScene;
        private set
        {
            if (_activeScene == value) return;
            if (_activeScene != null) _activeScene.IsActive = false;
            _activeScene = value;
            if (_activeScene != null) _activeScene.IsActive = true;
        }
    }

    public SceneItem CreateScene(string name)
    {
        var scene = new SceneItem(name);
        Scenes.Add(scene);

        if (ActiveScene == null)
            ActiveScene = scene;

        return scene;
    }

    public void DeleteScene(SceneItem scene)
    {
        if (Scenes.Count <= 1) return;

        foreach (var source in scene.Sources)
        {
            DestroySource(source);
        }

        Scenes.Remove(scene);

        if (ActiveScene == scene)
            ActiveScene = Scenes.Count > 0 ? Scenes[0] : null;
    }

    public void SetActiveScene(SceneItem scene)
    {
        if (!Scenes.Contains(scene)) return;
        ActiveScene = scene;
    }

    public SourceItem AddVideoSource(SceneItem scene, CaptureSourceOption source)
    {
        var color = source.Type switch
        {
            VideoCaptureKind.Monitor => "#5b8def",
            VideoCaptureKind.Camera => "#34d399",
            VideoCaptureKind.Window => "#8888a0",
            _ => "#5b8def"
        };

        var item = new SourceItem(source.Label, SourceKind.Video, color)
        {
            NativeDescriptor = source.Info
        };
        scene.Sources.Add(item);
        return item;
    }

    public SourceItem AddVideoSource(SceneItem scene, string label, string url)
    {
        var info = new CaptureSourceInfo
        {
            Label = label,
            Type = CaptureSourceType.Network,
            SymbolicLink = url,
            Index = -1
        };

        var item = new SourceItem(label, SourceKind.Video, "#5b8def")
        {
            NativeDescriptor = info
        };
        scene.Sources.Add(item);
        return item;
    }

    public SourceItem AddAudioSource(SceneItem scene, AudioSourceOption source)
    {
        var color = source.Type is AudioCaptureKind.Microphone or AudioCaptureKind.CameraMic
            ? "#f87171"
            : "#fbbf24";

        var item = new SourceItem(source.Label, SourceKind.Audio, color)
        {
            NativeDescriptor = source.Info
        };
        scene.Sources.Add(item);
        return item;
    }

    public void RemoveSource(SceneItem scene, SourceItem source)
    {
        DestroySource(source);
        scene.Sources.Remove(source);
    }

    private static void DestroySource(SourceItem source)
    {
        if (source.NativePtr == IntPtr.Zero) return;
        CastorNative.NativeDeactivateSource(source.NativePtr);
        CastorNative.NativeDestroySource(source.NativePtr);
        source.NativePtr = IntPtr.Zero;
    }
}
