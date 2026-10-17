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

    private RecordingState _recordingState;

    public RecordingState RecordingState
    {
        get => _recordingState;
        private set
        {
            if (!SetProperty(ref _recordingState, value)) return;
            OnPropertyChanged(nameof(IsRecording));
        }
    }

    // Paused and Stopping still count: the file stays open until libobs confirms the stop.
    public bool IsRecording => RecordingState != RecordingState.Idle;

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

    // Returns false, leaving the state untouched, when the move is not an allowed one.
    // Re-applying the current state is a no-op: runtime events and command results can
    // both report the same change.
    internal bool TrySetRecordingState(RecordingState next)
    {
        if (next == RecordingState) return true;
        if (!IsAllowedRecordingTransition(RecordingState, next)) return false;

        RecordingState = next;
        return true;
    }

    internal static bool IsAllowedRecordingTransition(RecordingState from, RecordingState to) =>
        (from, to) switch
        {
            (RecordingState.Idle, RecordingState.Recording) => true,
            (RecordingState.Recording, RecordingState.Paused) => true,
            (RecordingState.Paused, RecordingState.Recording) => true,
            (RecordingState.Recording or RecordingState.Paused, RecordingState.Stopping) => true,
            (RecordingState.Stopping, RecordingState.Idle) => true,
            _ => false
        };

    internal void SetStreamingState(bool value) => IsStreaming = value;

    partial void OnActiveSceneChanged(SceneItemViewModel? oldValue, SceneItemViewModel? newValue)
    {
        if (oldValue != null) oldValue.IsActive = false;
        if (newValue != null) newValue.IsActive = true;
    }
}
