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

    // The recordings running now. RecordingState sums them up (RefreshRecordingState).
    public ObservableCollection<RecordingSessionViewModel> RecordingSessions { get; } = [];

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

    // REC as long as one session records, paused once none does but one is paused, stopping
    // while the last ones finish. The overall state can jump further than one allowed step
    // (the only session dies on its own), so it walks the shortest allowed path there.
    internal void RefreshRecordingState()
    {
        var target = RecordingSessions.Count == 0
            ? RecordingState.Idle
            : RecordingSessions.Any(session => session.State == RecordingState.Recording)
                ? RecordingState.Recording
                : RecordingSessions.Any(session => session.State == RecordingState.Paused)
                    ? RecordingState.Paused
                    : RecordingState.Stopping;

        foreach (var step in RecordingTransitionPath(RecordingState, target))
            TrySetRecordingState(step);
        RefreshRecordedScenes();
    }

    internal static IReadOnlyList<RecordingState> RecordingTransitionPath(RecordingState from, RecordingState to)
    {
        var previous = new Dictionary<RecordingState, RecordingState>();
        var queue = new Queue<RecordingState>([from]);
        while (queue.Count > 0)
        {
            var state = queue.Dequeue();
            if (state == to) break;
            foreach (var next in Enum.GetValues<RecordingState>())
            {
                if (next == from || previous.ContainsKey(next) || !IsAllowedRecordingTransition(state, next)) continue;
                previous[next] = state;
                queue.Enqueue(next);
            }
        }

        var path = new List<RecordingState>();
        for (var state = to; state != from && previous.TryGetValue(state, out var before); state = before)
            path.Insert(0, state);
        return path;
    }

    // The program session records whichever scene is active; the others their own scene.
    private void RefreshRecordedScenes()
    {
        var followsProgram = RecordingSessions.Any(session => session.FollowsProgram);
        foreach (var scene in Scenes)
            scene.IsBeingRecorded =
                followsProgram && scene == ActiveScene ||
                RecordingSessions.Any(session => session.Scene == scene);
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
        RefreshRecordedScenes();
        foreach (var session in RecordingSessions) session.NotifyActiveSceneChanged();
    }
}
