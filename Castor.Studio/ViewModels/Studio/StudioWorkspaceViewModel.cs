using System.Collections.ObjectModel;
using CastorApplication.Models.Studio;
using CastorApplication.ViewModels.Scenes;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CastorApplication.ViewModels.Studio;

public partial class StudioWorkspaceViewModel : ViewModelBase
{
    public ObservableCollection<SceneItemViewModel> Scenes { get; } = [];

    [ObservableProperty]
    private SceneItemViewModel? _activeScene;

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private bool _isStreaming;

    public SceneItemViewModel CreateScene(string name)
    {
        var scene = new SceneItemViewModel(new SceneDefinition { Name = name.Trim() });
        Scenes.Add(scene);
        ActiveScene ??= scene;
        return scene;
    }

    public SceneItemViewModel AddScene(SceneDefinition definition)
    {
        var scene = new SceneItemViewModel(definition);
        Scenes.Add(scene);
        ActiveScene ??= scene;
        return scene;
    }

    public void DeleteScene(SceneItemViewModel scene)
    {
        if (!Scenes.Remove(scene)) return;
        if (ActiveScene == scene)
            ActiveScene = Scenes.FirstOrDefault();
    }

    public void SelectScene(SceneItemViewModel scene)
    {
        if (!Scenes.Contains(scene) || ActiveScene == scene) return;
        ActiveScene = scene;
    }

    // Reordering belongs to the workspace rather than to whichever page asked for it,
    // so the order holds everywhere the scenes are listed.
    public void MoveScene(SceneItemViewModel moved, SceneItemViewModel target)
    {
        if (ReferenceEquals(moved, target)) return;

        var from = Scenes.IndexOf(moved);
        var to = Scenes.IndexOf(target);
        if (from < 0 || to < 0) return;

        Scenes.Move(from, to);
    }

    public SourceItemViewModel AddSource(SceneItemViewModel scene, SourceDefinition definition)
    {
        var source = new SourceItemViewModel(definition);
        scene.Sources.Add(source);
        return source;
    }

    public static bool HasVideoSource(SceneItemViewModel scene) => scene.HasVideoSource;

    internal void SetRecordingState(bool value) => IsRecording = value;
    internal void SetStreamingState(bool value) => IsStreaming = value;

    partial void OnActiveSceneChanged(SceneItemViewModel? oldValue, SceneItemViewModel? newValue)
    {
        if (oldValue != null) oldValue.IsActive = false;
        if (newValue != null) newValue.IsActive = true;
    }
}
