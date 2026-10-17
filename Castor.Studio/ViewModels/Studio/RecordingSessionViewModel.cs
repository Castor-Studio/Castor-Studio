using CastorApplication.Models.Studio;
using CastorApplication.ViewModels.Scenes;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CastorApplication.ViewModels.Studio;

// One running recording, as listed under the recording controls: its own file, state, timer
// and actions. The program-following one shows whichever scene is active.
public sealed partial class RecordingSessionViewModel : ViewModelBase
{
    private readonly StudioWorkspaceViewModel _workspace;
    private readonly RecordingClock _clock;

    internal RecordingSessionViewModel(
        Guid id,
        StudioWorkspaceViewModel workspace,
        SceneItemViewModel? scene,
        string outputPath,
        string pauseUnavailableReason,
        TimeProvider timeProvider)
    {
        Id = id;
        _workspace = workspace;
        Scene = scene;
        OutputPath = outputPath;
        PauseUnavailableReason = pauseUnavailableReason;
        _clock = new RecordingClock(timeProvider);
        _clock.Start();
    }

    public Guid Id { get; }

    // Null for the session that follows the program.
    public SceneItemViewModel? Scene { get; }
    public bool FollowsProgram => Scene == null;
    public string OutputPath { get; }
    public string PauseUnavailableReason { get; }
    public bool CanPause => PauseUnavailableReason.Length == 0;
    // Null rather than "": an empty tip would still pop up as a blank box.
    public string? PauseToolTip => CanPause ? null : PauseUnavailableReason;

    public string Name => Scene?.Name ?? "Scène active";
    public string Detail => FollowsProgram
        ? $"En ce moment : {_workspace.ActiveScene?.Name ?? "aucune scène"}"
        : "Enregistrée à part, dans son propre fichier";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(PauseButtonText), nameof(IsPaused), nameof(IsActive))]
    private RecordingState _state = RecordingState.Recording;

    [ObservableProperty]
    private string _timerText = "00:00:00";

    public bool IsPaused => State == RecordingState.Paused;
    public bool IsActive => State is RecordingState.Recording or RecordingState.Paused;
    public string PauseButtonText => IsPaused ? "Reprendre" : "Pause";
    public string StateText => State switch
    {
        RecordingState.Paused => "En pause",
        RecordingState.Stopping => "Arrêt…",
        RecordingState.Idle => "Arrêté",
        _ => "En cours"
    };

    // Same rules as the workspace state. A session that ends on its own reports Idle
    // straight from Recording / Paused and goes through Stopping; anything else invalid is
    // ignored, so a late event cannot revive a stopping session.
    internal void Apply(RecordingState next)
    {
        if (next == RecordingState.Idle && State is RecordingState.Recording or RecordingState.Paused)
            State = RecordingState.Stopping;
        if (next != State && !StudioWorkspaceViewModel.IsAllowedRecordingTransition(State, next)) return;

        State = next;
        _clock.SetPaused(State == RecordingState.Paused);
        RefreshTimer();
    }

    internal void RefreshTimer() => TimerText = _clock.Text;

    internal void NotifyActiveSceneChanged()
    {
        if (FollowsProgram) OnPropertyChanged(nameof(Detail));
    }
}
