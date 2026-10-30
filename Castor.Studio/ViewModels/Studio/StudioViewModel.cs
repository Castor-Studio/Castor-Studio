using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Threading;
using CastorApplication.Models.Settings;
using CastorApplication.Models.Settings.Providers;
using CastorApplication.Models.Studio;
using CastorApplication.Services;
using CastorApplication.Services.Dialogs;
using CastorApplication.Services.Auth.Storage;
using CastorApplication.Services.Settings;
using CastorApplication.Services.Studio;
using CastorApplication.ViewModels.Scenes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.ViewModels.Studio;

public partial class StudioViewModel : ViewModelBase
{
    private readonly StudioWorkspaceViewModel _workspace;
    private readonly IStudioRuntime _runtime;
    private readonly IScenePreviewRuntime _previewRuntime;
    private readonly IRecordingRuntime _recordingRuntime;
    private readonly IStreamingRuntime _streamingRuntime;
    private readonly IProviderStore _providerStore;
    private readonly SettingsService _settingsService;
    private readonly VideoCanvasResolutionResolver _resolutionResolver;
    private readonly DispatcherTimer _sessionTimer;
    private readonly TimeProvider _timeProvider;
    // Without a dialog to confirm with, cancelling is not offered: it deletes a file.
    private readonly ICancelRecordingDialogService? _cancelRecordingDialog;
    private readonly RecordingClock _sessionClock;
    private bool _loadingTransition;

    public ObservableCollection<SceneItemViewModel> Scenes => _workspace.Scenes;
    public bool IsStreaming => _workspace.IsStreaming;
    public RecordingState RecordingState => _workspace.RecordingState;
    public bool IsRecording => _workspace.IsRecording;

    public SceneItemViewModel? ActiveScene
    {
        get => _workspace.ActiveScene;
        set
        {
            if (value == null) return;
            _workspace.SelectScene(value);
            OnPropertyChanged();
            NotifyPreviewChanged();
        }
    }

    // Backs the native preview host in PreviewPaneView.axaml - same engine and same base
    // resolution tracking as ScenesViewModel. Each page gets its own native display, so
    // this one follows the active scene while the Scenes page follows the one being edited.
    public IScenePreviewRuntime PreviewRuntime => _previewRuntime;

    [ObservableProperty] private int _baseCanvasWidth = 1920;
    [ObservableProperty] private int _baseCanvasHeight = 1080;

    public string PreviewPlaceholderText => !_previewRuntime.IsAvailable
        ? _previewRuntime.UnavailableMessage
        : ActiveScene == null
            ? "Aucune scène active — créez-en une dans l'onglet Scènes."
            : !StudioWorkspaceViewModel.HasVideoSource(ActiveScene)
                ? "Cette scène n'a pas de source vidéo."
                : "";

    public bool ShowPreviewPlaceholder => PreviewPlaceholderText.Length > 0;

    // The preview's empty screens. An unavailable engine is not one: the preview itself
    // says what is wrong.
    public bool ShowNoScene => _previewRuntime.IsAvailable && ActiveScene == null;
    public bool ShowNoVideoSource => _previewRuntime.IsAvailable && ActiveScene != null
        && !StudioWorkspaceViewModel.HasVideoSource(ActiveScene);
    public bool ShowEmptyPreview => ShowNoScene || ShowNoVideoSource;

    // Change from one scene to another on live and recording. The preview always hard-cuts:
    // it shows the active scene, not the program output.
    public IReadOnlyList<SceneTransitionOption> TransitionOptions => SceneTransitionOption.All;
    public IReadOnlyList<int> TransitionDurations => SceneTransitionOption.Durations;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTransitionAnimated), nameof(TransitionDurationText))]
    private SceneTransitionOption? _selectedTransition = SceneTransitionOption.For(SceneTransition.Default.Kind);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TransitionDurationStep), nameof(TransitionDurationText))]
    private int _transitionDurationMs = SceneTransition.DefaultDurationMs;

    // A cut has no duration: the duration slider grays out.
    public bool IsTransitionAnimated => SelectedTransition?.Kind != SceneTransitionKind.Cut;

    // The slider moves from preset to preset, not millisecond by millisecond: a drag only writes
    // settings on each step, and always lands on a round value.
    public double TransitionDurationStep
    {
        get => SceneTransitionOption.StepOf(TransitionDurationMs);
        set => TransitionDurationMs = SceneTransitionOption.DurationAt((int)Math.Round(value));
    }

    public int TransitionDurationLastStep => TransitionDurations.Count - 1;

    public string TransitionDurationText => IsTransitionAnimated ? $"{TransitionDurationMs} ms" : "";

    [ObservableProperty] private string _streamTimerText = "00:00:00";
    [ObservableProperty] private bool _isStreamingTransition;
    [ObservableProperty] private string _connectedAccountLabel = "Compte Twitch non connecté";

    // Without a connected account the live cannot start, so the go-live panel says so up front:
    // DÉMARRER is disabled with the reason, and the account area turns into a way to connect one.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartStreamingCommand))]
    [NotifyPropertyChangedFor(nameof(StartStreamingBlockedReason), nameof(DestinationLabel))]
    private bool _isAccountConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DestinationLabel))]
    private string _connectedAccountName = "";

    public string StartStreamingBlockedReason =>
        IsAccountConnected ? "" : "Connectez un compte Twitch pour lancer le live.";

    // Filled in with the account as soon as one is connected, including after a round trip to
    // Settings > Comptes from the panel.
    public string DestinationLabel => IsAccountConnected ? $"Twitch · {ConnectedAccountName}" : "Twitch";

    // The shell answers by opening Settings on the accounts section (MainViewModel).
    public event EventHandler? AccountSettingsRequested;

    [RelayCommand]
    private void OpenAccountSettings() => AccountSettingsRequested?.Invoke(this, EventArgs.Empty);

    // The empty preview leads to the Scenes page, where scenes and sources are made (MainViewModel).
    public event EventHandler? ScenesPageRequested;

    [RelayCommand]
    private void OpenScenes() => ScenesPageRequested?.Invoke(this, EventArgs.Empty);

    // Errors only: they stay until dismissed (closing their flyout) or until the scene or the
    // page changes, so a failure from an earlier attempt is not shown as a current one.
    public StatusMessage RecordError { get; }
    public StatusMessage StreamError { get; }
    [ObservableProperty] private string _outputInfoText = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordingButtonToolTip))]
    private string _recordingOutputDirectory = "";

    public string StreamStatusText => IsStreaming ? "EN DIRECT" : "OFFLINE";
    public string SceneBarStatusText => IsStreaming
        ? "En direct"
        : IsRecordingPaused ? "En pause" : IsRecording ? "Enregistrement" : "Prêt";
    public bool IsRecordingPaused => _workspace.RecordingState == RecordingState.Paused;

    public ObservableCollection<RecordingSessionViewModel> RecordingSessions => _workspace.RecordingSessions;

    // Each session's container and encoder decide whether it can pause; the global pause
    // is offered as long as one of them can.
    public string PauseUnavailableReason =>
        RecordingSessions.Count > 0 && !RecordingSessions.Any(session => session.CanPause)
            ? RecordingSessions[0].PauseUnavailableReason
            : "";

    public const string PauseShortcutText = "Ctrl+Maj+P";

    public bool CanTogglePauseRecording =>
        PauseUnavailableReason.Length == 0 &&
        _workspace.RecordingState is RecordingState.Recording or RecordingState.Paused;

    public string PauseButtonText => IsRecordingPaused ? "REPRENDRE" : "PAUSE";

    private bool HasSeveralSessions => RecordingSessions.Count > 1;

    public string PauseToolTip => PauseUnavailableReason.Length > 0
        ? PauseUnavailableReason
        : (IsRecordingPaused, HasSeveralSessions) switch
        {
            (true, true) => $"Reprendre tous les enregistrements ({PauseShortcutText})",
            (true, false) => $"Reprendre l'enregistrement dans le même fichier ({PauseShortcutText})",
            (false, true) => $"Mettre tous les enregistrements en pause ({PauseShortcutText})",
            _ => $"Mettre l'enregistrement en pause ({PauseShortcutText})"
        };

    public string CancelAllToolTip => HasSeveralSessions
        ? "Arrêter tous les enregistrements et supprimer leurs fichiers"
        : "Arrêter l'enregistrement et supprimer son fichier";

    public string RecordingSessionsToolTip => RecordingSessions.Count == 1
        ? "Enregistrement en cours"
        : $"{RecordingSessions.Count} enregistrements en cours";

    // ── Before recording: what to record ─────────────────────────────────────────────
    // The program alone by default, so ENREGISTRER stays one click. Ticked scenes are each
    // recorded into a file of their own, with or without the program.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordingTargetCount), nameof(RecordingSelectionSummary),
        nameof(RecordingLoadWarning), nameof(RecordingButtonToolTip), nameof(HasRecordingTarget))]
    [NotifyCanExecuteChangedFor(nameof(StartRecordingCommand))]
    private bool _recordProgram = true;

    public ObservableCollection<RecordingSceneOption> RecordingSceneOptions { get; } = [];

    public int RecordingTargetCount =>
        (RecordProgram ? 1 : 0) + RecordingSceneOptions.Count(option => option.IsSelected);

    public bool HasRecordingTarget => RecordingTargetCount > 0;

    // Each extra scene is rendered and encoded on its own (x264, on the processor).
    public const int HeavyRecordingTargetCount = 3;

    public string RecordingLoadWarning => RecordingTargetCount >= HeavyRecordingTargetCount
        ? $"{RecordingTargetCount} enregistrements à la fois : chaque scène est rendue et encodée à part, " +
          "le processeur et la carte graphique risquent de saturer et de perdre des images."
        : "";

    public string RecordingSelectionSummary
    {
        get
        {
            var scenes = RecordingSceneOptions.Count(option => option.IsSelected);
            var sceneText = scenes == 1 ? "1 scène" : $"{scenes} scènes";
            return (RecordProgram, scenes) switch
            {
                (_, 0) when RecordProgram => "la scène active",
                (true, _) => $"la scène active et {sceneText} à part",
                (false, 0) => "rien",
                _ => $"{sceneText} à part"
            };
        }
    }

    public string RecordingButtonToolTip => HasRecordingTarget
        ? $"Enregistre {RecordingSelectionSummary} dans : {RecordingOutputDirectory}"
        : "Choisissez au moins une scène à enregistrer.";
    // Colours come from the "session-state" styles (Controls.axaml), so they follow the theme.
    public bool IsOnAir => IsStreaming || IsRecording;

    internal StudioViewModel(
        StudioWorkspaceViewModel workspace,
        IStudioRuntime runtime,
        IScenePreviewRuntime previewRuntime,
        IRecordingRuntime recordingRuntime,
        IStreamingRuntime streamingRuntime,
        IProviderStore providerStore,
        SettingsService settingsService,
        VideoCanvasResolutionResolver? resolutionResolver = null,
        StatusMessageService? messages = null,
        TimeProvider? timeProvider = null,
        ICancelRecordingDialogService? cancelRecordingDialog = null)
    {
        _cancelRecordingDialog = cancelRecordingDialog;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _sessionClock = new RecordingClock(_timeProvider);
        messages ??= new StatusMessageService();
        RecordError = messages.Create();
        StreamError = messages.Create();
        _workspace = workspace;
        _runtime = runtime;
        _previewRuntime = previewRuntime;
        _recordingRuntime = recordingRuntime;
        _streamingRuntime = streamingRuntime;
        _providerStore = providerStore;
        _settingsService = settingsService;
        _resolutionResolver = resolutionResolver ?? new VideoCanvasResolutionResolver(settingsService);
        _workspace.PropertyChanged += OnWorkspacePropertyChanged;
        _workspace.Scenes.CollectionChanged += (_, _) => SyncRecordingSceneOptions();
        _workspace.RecordingSessions.CollectionChanged += (_, _) => NotifyRecordingSessionsChanged();
        SyncRecordingSceneOptions();
        _recordingRuntime.StateChanged += OnRecordingRuntimeStateChanged;
        _streamingRuntime.StreamingStateChanged += OnStreamingRuntimeStateChanged;
        _providerStore.Changed += OnProviderStoreChanged;
        _settingsService.SettingsSaved += OnSettingsSaved;
        _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sessionTimer.Tick += OnSessionTimerTick;
        RefreshProviderState();
        RefreshOutputInfo();
        RefreshBaseCanvasSize();
        RefreshTransition();
    }

    public void RefreshOutputInfo()
    {
        var settings = _settingsService.Load();
        var (width, height) = OutputResolutionFromIndex(settings.SelectedOutputResolutionIndex);
        OutputInfoText = $"{width} × {height} @ {FpsFromIndex(settings.SelectedFpsIndex)} fps";
        RecordingOutputDirectory = string.IsNullOrWhiteSpace(settings.OutputPath)
            ? ApplicationSettings.DefaultOutputPath
            : settings.OutputPath;
    }

    // Keeps the native preview's aspect ratio correct when the base resolution changes,
    // same as ScenesViewModel's own OnSettingsSaved. The output info follows too, so the
    // ENREGISTRER tooltip names the new folder even from a panel detached beside Settings.
    private void OnSettingsSaved(object? sender, EventArgs e)
    {
        RefreshBaseCanvasSize();
        RefreshOutputInfo();
        RefreshTransition();
    }

    // Type and duration reload one after the other: without this guard, the first would be saved
    // with the second's old value.
    private void RefreshTransition()
    {
        _loadingTransition = true;
        try
        {
            var transition = _settingsService.Load().ToSceneTransition();
            SelectedTransition = SceneTransitionOption.For(transition.Kind);
            TransitionDurationMs = SceneTransitionOption.NearestDuration(transition.DurationMs);
        }
        finally
        {
            _loadingTransition = false;
        }
    }

    partial void OnSelectedTransitionChanged(SceneTransitionOption? value) => SaveTransition();

    partial void OnTransitionDurationMsChanged(int value) => SaveTransition();

    // The runtime rereads the transition on every save: the next scene change uses it, a running
    // live included.
    private void SaveTransition()
    {
        // A ComboBox being emptied pushes null: nothing chosen, nothing to save.
        if (_loadingTransition || SelectedTransition == null) return;

        var settings = _settingsService.Load();
        if (settings.SceneTransitionKind == SelectedTransition.Kind &&
            settings.SceneTransitionDurationMs == TransitionDurationMs)
            return;

        settings.SceneTransitionKind = SelectedTransition.Kind;
        settings.SceneTransitionDurationMs = TransitionDurationMs;
        _settingsService.Save(settings);
    }

    private void RefreshBaseCanvasSize()
    {
        var resolution = _resolutionResolver.Resolve(_settingsService.Load());
        BaseCanvasWidth = resolution.Width;
        BaseCanvasHeight = resolution.Height;
    }

    [RelayCommand(CanExecute = nameof(IsAccountConnected))]
    private async Task StartStreaming(CancellationToken cancellationToken)
    {
        StreamError.Clear();
        if (!_streamingRuntime.IsAvailable)
        {
            StreamError.ShowError(_streamingRuntime.UnavailableMessage);
            return;
        }

        if (IsRecording)
        {
            StreamError.ShowError("Arrêtez l'enregistrement avant de lancer le live.");
            return;
        }

        var scene = ActiveScene;
        if (scene == null || !StudioWorkspaceViewModel.HasVideoSource(scene))
        {
            StreamError.ShowError("Aucune source vidéo dans la scène active.");
            return;
        }

        var provider = GetConnectedTwitchProvider();
        if (provider == null)
        {
            StreamError.ShowError(AccountDisconnectedError);
            RefreshProviderState();
            return;
        }

        var settings = _settingsService.Load();
        IsStreamingTransition = true;
        try
        {
            var result = await _streamingRuntime.StartStreamingAsync(new StreamingRequest(
                scene.ToDefinition(),
                provider.StreamKey!,
                FpsFromIndex(settings.SelectedFpsIndex),
                (int)settings.StreamingBitrate,
                AudioBitrateFromIndex(settings.SelectedAudioBitrateIndex),
                AudioSampleRateFromIndex(settings.SelectedSampleRateIndex),
                AudioChannelsFromIndex(settings.SelectedChannelsIndex)), cancellationToken);
            if (!result.IsSuccess)
            {
                StreamError.ShowError(result.Message);
                return;
            }

            _workspace.SetStreamingState(true);
        }
        finally
        {
            IsStreamingTransition = false;
        }
    }

    [RelayCommand]
    private async Task StopStreaming(CancellationToken cancellationToken)
    {
        IsStreamingTransition = true;
        try
        {
            var result = await _streamingRuntime.StopStreamingAsync(cancellationToken);
            if (!result.IsSuccess)
            {
                StreamError.ShowError(result.Message);
                return;
            }
            _workspace.SetStreamingState(false);
        }
        finally
        {
            IsStreamingTransition = false;
        }
    }

    // One session per target: the program (following the active scene) if ticked, then each
    // ticked scene into a file of its own. A target that fails is reported by name while the
    // others keep recording.
    [RelayCommand(CanExecute = nameof(HasRecordingTarget))]
    private async Task StartRecording(CancellationToken cancellationToken)
    {
        RecordError.Clear();
        if (IsStreaming)
        {
            RecordError.ShowError("Arrêtez le live avant de démarrer l'enregistrement.");
            return;
        }

        if (!_recordingRuntime.IsAvailable)
        {
            RecordError.ShowError(_recordingRuntime.UnavailableMessage);
            return;
        }

        var targets = new List<SceneItemViewModel?>();
        if (RecordProgram) targets.Add(null);
        targets.AddRange(RecordingSceneOptions.Where(option => option.IsSelected).Select(option => option.Scene));
        if (targets.Count == 0)
        {
            RecordError.ShowError("Choisissez au moins une scène à enregistrer.");
            return;
        }

        var settings = _settingsService.Load();
        var container = FormatFromIndex(settings.SelectedOutputFormatIndex);
        var baseResolution = _resolutionResolver.Resolve(settings);
        var (width, height) = OutputResolutionFromIndex(settings.SelectedOutputResolutionIndex);
        var timestamp = DateTime.Now;
        var takenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();
        string Describe(SceneItemViewModel? target, string message) =>
            targets.Count == 1 ? message : $"{target?.Name ?? "Scène active"} : {message}";

        foreach (var target in targets)
        {
            var scene = target ?? ActiveScene;
            if (scene == null || !StudioWorkspaceViewModel.HasVideoSource(scene))
            {
                failures.Add(Describe(target, target == null
                    ? "Aucune source vidéo dans la scène active."
                    : "Cette scène n'a pas de source vidéo."));
                continue;
            }

            string path;
            try
            {
                path = RecordingOutputPath.Create(settings.OutputPath, container, timestamp, target?.Name, takenPaths);
            }
            catch (Exception exception)
            {
                failures.Add(Describe(target, exception.Message));
                continue;
            }

            takenPaths.Add(path);
            RecordingOutputDirectory = Path.GetDirectoryName(path) ?? "";
            var sessionId = Guid.NewGuid();
            var result = await _recordingRuntime.StartRecordingAsync(new RecordingRequest(
                scene.Id,
                path,
                FpsFromIndex(settings.SelectedFpsIndex),
                (int)settings.VideoBitrate,
                AudioBitrateFromIndex(settings.SelectedAudioBitrateIndex),
                AudioSampleRateFromIndex(settings.SelectedSampleRateIndex),
                AudioChannelsFromIndex(settings.SelectedChannelsIndex),
                baseResolution.Width,
                baseResolution.Height,
                width,
                height,
                container,
                sessionId,
                FollowsProgram: target == null), cancellationToken);
            if (!result.IsSuccess)
            {
                failures.Add(Describe(target, result.Message));
                continue;
            }

            _workspace.RecordingSessions.Add(new RecordingSessionViewModel(
                sessionId,
                _workspace,
                target,
                path,
                _recordingRuntime.GetPauseUnavailableReason(sessionId),
                _timeProvider));
            _workspace.RefreshRecordingState();
        }

        if (failures.Count > 0) RecordError.ShowError(string.Join(Environment.NewLine, failures));
    }

    // Prefixed with the session's name once several are running, so the message says which.
    private string DescribeSession(RecordingSessionViewModel session, string message) =>
        HasSeveralSessions ? $"{session.Name} : {message}" : message;

    private void ApplySessionState(RecordingSessionViewModel session, RecordingState state)
    {
        session.Apply(state);
        // The list drops a session as soon as it is over.
        if (session.State == RecordingState.Idle) _workspace.RecordingSessions.Remove(session);
        _workspace.RefreshRecordingState();
        NotifySessionCommandsChanged();
    }

    // ── Global actions: every session at once ────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanTogglePauseRecording))]
    private void TogglePauseRecording()
    {
        RecordError.Clear();
        var pausing = _workspace.RecordingState == RecordingState.Recording;
        var sessions = RecordingSessions
            .Where(session => session.CanPause &&
                              session.State == (pausing ? RecordingState.Recording : RecordingState.Paused))
            .ToList();
        var failures = sessions
            .Select(session => SetSessionPaused(session, pausing))
            .Where(failure => failure.Length > 0)
            .ToList();
        if (failures.Count > 0) RecordError.ShowError(string.Join(Environment.NewLine, failures));
    }

    [RelayCommand]
    private Task StopRecording(CancellationToken cancellationToken) =>
        StopSessionsAsync([.. RecordingSessions], cancellationToken);

    public bool CanCancelRecording =>
        _cancelRecordingDialog != null &&
        RecordingSessions.Any(session => session.IsActive);

    [RelayCommand(CanExecute = nameof(CanCancelRecording))]
    private Task CancelRecording(CancellationToken cancellationToken) =>
        CancelSessionsAsync([.. RecordingSessions.Where(session => session.IsActive)], cancellationToken);

    // ── One session ──────────────────────────────────────────────────────────────────

    private static bool CanToggleSessionPause(RecordingSessionViewModel? session) =>
        session is { IsActive: true, CanPause: true };

    [RelayCommand(CanExecute = nameof(CanToggleSessionPause))]
    private void ToggleSessionPause(RecordingSessionViewModel? session)
    {
        if (session == null) return;
        RecordError.Clear();
        var failure = SetSessionPaused(session, !session.IsPaused);
        if (failure.Length > 0) RecordError.ShowError(failure);
    }

    private static bool CanStopSession(RecordingSessionViewModel? session) => session is { IsActive: true };

    [RelayCommand(CanExecute = nameof(CanStopSession))]
    private Task StopSession(RecordingSessionViewModel? session, CancellationToken cancellationToken) =>
        session == null ? Task.CompletedTask : StopSessionsAsync([session], cancellationToken);

    private bool CanCancelSession(RecordingSessionViewModel? session) =>
        _cancelRecordingDialog != null && session is { IsActive: true };

    [RelayCommand(CanExecute = nameof(CanCancelSession))]
    private Task CancelSession(RecordingSessionViewModel? session, CancellationToken cancellationToken) =>
        session == null ? Task.CompletedTask : CancelSessionsAsync([session], cancellationToken);

    // "" on success. On failure libobs left the output as it was, so the state stays too.
    private string SetSessionPaused(RecordingSessionViewModel session, bool paused)
    {
        var result = paused
            ? _recordingRuntime.PauseRecording(session.Id)
            : _recordingRuntime.ResumeRecording(session.Id);
        if (!result.IsSuccess) return DescribeSession(session, result.Message);

        // The runtime also reports the change; applying it twice is a no-op.
        ApplySessionState(session, paused ? RecordingState.Paused : RecordingState.Recording);
        return "";
    }

    // The stops run side by side, each returning once its output released the file; their
    // results are applied together afterwards, on the caller's thread.
    private async Task<IReadOnlyList<RecordingSessionViewModel>> StopSessionsCoreAsync(
        IReadOnlyList<RecordingSessionViewModel> sessions,
        CancellationToken cancellationToken)
    {
        // A session that ended on its own (while a dialog was open) has nothing left to stop.
        var running = sessions.Where(RecordingSessions.Contains).ToList();
        var results = await Task.WhenAll(running.Select(session =>
            _recordingRuntime.StopRecordingAsync(session.Id, cancellationToken)));

        var failures = new List<string>();
        var stillRunning = new List<RecordingSessionViewModel>();
        for (var i = 0; i < running.Count; i++)
        {
            var session = running[i];
            if (!results[i].IsSuccess && session.State is not RecordingState.Idle && RecordingSessions.Contains(session))
            {
                failures.Add(DescribeSession(session, results[i].Message));
                stillRunning.Add(session);
                continue;
            }
            ApplySessionState(session, RecordingState.Idle);
        }

        if (failures.Count > 0) RecordError.ShowError(string.Join(Environment.NewLine, failures));
        return stillRunning;
    }

    private async Task StopSessionsAsync(IReadOnlyList<RecordingSessionViewModel> sessions, CancellationToken cancellationToken)
    {
        RecordError.Clear();
        await StopSessionsCoreAsync(sessions, cancellationToken);
    }

    // Throws takes away: stop, then delete the files these sessions created - and only those,
    // whose names RecordingOutputPath picked so that they did not exist beforehand.
    private async Task CancelSessionsAsync(IReadOnlyList<RecordingSessionViewModel> sessions, CancellationToken cancellationToken)
    {
        if (_cancelRecordingDialog == null || sessions.Count == 0) return;
        if (!await _cancelRecordingDialog.ConfirmAsync([.. sessions.Select(session => session.OutputPath)])) return;

        RecordError.Clear();
        // Still running: their files are in use, so they stay, like the recordings.
        var stillRunning = await StopSessionsCoreAsync(sessions, cancellationToken);

        var undeleted = new List<string>();
        foreach (var session in sessions.Except(stillRunning))
        {
            if (!await RecordingFile.TryDeleteAsync(session.OutputPath))
                undeleted.Add(session.OutputPath);
        }

        if (undeleted.Count == 0) return;
        var files = undeleted.Count == 1
            ? $"son fichier n'a pas pu être supprimé. Il est peut-être ouvert dans une autre application, " +
              $"ou protégé en écriture. Supprimez-le à la main : {undeleted[0]}"
            : "ces fichiers n'ont pas pu être supprimés. Ils sont peut-être ouverts dans une autre application, " +
              $"ou protégés en écriture. Supprimez-les à la main :{Environment.NewLine}{string.Join(Environment.NewLine, undeleted)}";
        var message = $"Enregistrement annulé, mais {files}";
        RecordError.ShowError(RecordError.Text.Length > 0 ? $"{RecordError.Text}{Environment.NewLine}{message}" : message);
    }

    private void OnRecordingRuntimeStateChanged(object? sender, RecordingStateChangedEventArgs e)
    {
        void ApplyState()
        {
            // Events of a session not listed yet (its start is still being awaited) or gone
            // already are dropped: the start result and the stop result cover them.
            var session = RecordingSessions.FirstOrDefault(candidate => candidate.Id == e.SessionId);
            if (session == null) return;
            var message = string.IsNullOrWhiteSpace(e.Message) ? "" : DescribeSession(session, e.Message);
            ApplySessionState(session, e.State);
            if (message.Length > 0) RecordError.ShowError(message);
        }

        if (Application.Current == null || Dispatcher.UIThread.CheckAccess()) ApplyState();
        else Dispatcher.UIThread.Post(ApplyState);
    }

    // Keeps one option per scene, in the scenes' order, ticks preserved.
    private void SyncRecordingSceneOptions()
    {
        var existing = RecordingSceneOptions.ToDictionary(option => option.Scene);
        RecordingSceneOptions.Clear();
        foreach (var scene in _workspace.Scenes)
        {
            if (!existing.TryGetValue(scene, out var option))
            {
                option = new RecordingSceneOption(scene);
                option.PropertyChanged += (_, _) => NotifyRecordingSelectionChanged();
            }
            RecordingSceneOptions.Add(option);
        }
        NotifyRecordingSelectionChanged();
    }

    private void NotifyRecordingSelectionChanged()
    {
        OnPropertyChanged(nameof(RecordingTargetCount));
        OnPropertyChanged(nameof(HasRecordingTarget));
        OnPropertyChanged(nameof(RecordingSelectionSummary));
        OnPropertyChanged(nameof(RecordingLoadWarning));
        OnPropertyChanged(nameof(RecordingButtonToolTip));
        StartRecordingCommand.NotifyCanExecuteChanged();
    }

    private void NotifyRecordingSessionsChanged()
    {
        OnPropertyChanged(nameof(PauseUnavailableReason));
        OnPropertyChanged(nameof(CanTogglePauseRecording));
        OnPropertyChanged(nameof(PauseToolTip));
        OnPropertyChanged(nameof(RecordingSessionsToolTip));
        OnPropertyChanged(nameof(CancelAllToolTip));
        NotifySessionCommandsChanged();
    }

    private void NotifySessionCommandsChanged()
    {
        TogglePauseRecordingCommand.NotifyCanExecuteChanged();
        CancelRecordingCommand.NotifyCanExecuteChanged();
        ToggleSessionPauseCommand.NotifyCanExecuteChanged();
        StopSessionCommand.NotifyCanExecuteChanged();
        CancelSessionCommand.NotifyCanExecuteChanged();
    }

    private void OnStreamingRuntimeStateChanged(object? sender, StreamingStateChangedEventArgs e)
    {
        void ApplyState()
        {
            _workspace.SetStreamingState(e.IsStreaming);
            if (!string.IsNullOrWhiteSpace(e.Message)) StreamError.ShowError(e.Message);
        }

        if (Application.Current == null || Dispatcher.UIThread.CheckAccess()) ApplyState();
        else Dispatcher.UIThread.Post(ApplyState);
    }

    private void OnProviderStoreChanged(object? sender, EventArgs e)
    {
        if (Application.Current == null || Dispatcher.UIThread.CheckAccess())
            RefreshProviderState();
        else
            Dispatcher.UIThread.Post(RefreshProviderState);
    }

    // A scene switch has to reach the running output, not just the preview: the recorded
    // file and live stream render whatever sits in the OBS program channel. Idle is left
    // alone - starting an output points the channel at the active scene anyway.
    private void ApplyActiveSceneToRecording()
    {
        // Sessions dedicated to a scene keep their scene; only the program one follows.
        if (!RecordingSessions.Any(session => session.FollowsProgram)) return;

        var scene = ActiveScene;
        if (scene == null) return;

        var result = _recordingRuntime.SwitchRecordingScene(scene.Id);
        if (!result.IsSuccess) RecordError.ShowError(result.Message);
    }

    private void ApplyActiveSceneToStreaming()
    {
        if (!_workspace.IsStreaming) return;

        var scene = ActiveScene;
        if (scene == null) return;

        var result = _streamingRuntime.SwitchStreamingScene(scene.Id);
        if (!result.IsSuccess) StreamError.ShowError(result.Message);
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StudioWorkspaceViewModel.ActiveScene))
        {
            OnPropertyChanged(nameof(ActiveScene));
            NotifyPreviewChanged();
            // Whatever went wrong was about the previous scene; a failed switch reports again.
            RecordError.Clear();
            StreamError.Clear();
            ApplyActiveSceneToRecording();
            ApplyActiveSceneToStreaming();
        }
        else if (e.PropertyName is nameof(StudioWorkspaceViewModel.RecordingState) or nameof(StudioWorkspaceViewModel.IsStreaming))
        {
            NotifySessionStateChanged();
            if (IsRecording || IsStreaming)
            {
                StartSessionTimerIfNeeded();
                TrackRecordingPause();
            }
            else
            {
                ResetSessionTimer();
            }

            NotifySessionCommandsChanged();
        }
    }

    private void NotifyPreviewChanged()
    {
        OnPropertyChanged(nameof(PreviewPlaceholderText));
        OnPropertyChanged(nameof(ShowPreviewPlaceholder));
        OnPropertyChanged(nameof(ShowNoScene));
        OnPropertyChanged(nameof(ShowNoVideoSource));
        OnPropertyChanged(nameof(ShowEmptyPreview));
    }

    private void NotifySessionStateChanged()
    {
        OnPropertyChanged(nameof(IsStreaming));
        OnPropertyChanged(nameof(RecordingState));
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(StreamStatusText));
        OnPropertyChanged(nameof(IsOnAir));
        OnPropertyChanged(nameof(SceneBarStatusText));
        OnPropertyChanged(nameof(IsRecordingPaused));
        OnPropertyChanged(nameof(PauseButtonText));
        OnPropertyChanged(nameof(CanTogglePauseRecording));
        OnPropertyChanged(nameof(PauseToolTip));
    }

    private const string AccountDisconnectedError = "Compte Twitch déconnecté. Reconnectez-vous dans Paramètres → Comptes.";

    private ProviderSettings? GetConnectedTwitchProvider()
    {
        var provider = _providerStore.Get("twitch");
        return provider is { IsConnected: true } && !string.IsNullOrWhiteSpace(provider.StreamKey)
            ? provider
            : null;
    }

    private void RefreshProviderState()
    {
        var provider = GetConnectedTwitchProvider();
        ConnectedAccountLabel = provider == null
            ? "Compte Twitch non connecté"
            : $"Connecté en tant que {provider.UserName}";
        ConnectedAccountName = provider?.UserName ?? "";
        IsAccountConnected = provider != null;

        // An account error left from an earlier attempt no longer applies once one is connected.
        if (provider != null && StreamError.Text == AccountDisconnectedError) StreamError.Clear();
    }

    private void StartSessionTimerIfNeeded()
    {
        if (_sessionClock.IsStarted) return;
        _sessionClock.Start();
        StreamTimerText = "00:00:00";
        _sessionTimer.Start();
    }

    private void TrackRecordingPause()
    {
        _sessionClock.SetPaused(IsRecordingPaused);
        RefreshSessionTimer();
    }

    private void ResetSessionTimer()
    {
        _sessionTimer.Stop();
        _sessionClock.Reset();
        StreamTimerText = "00:00:00";
    }

    private void OnSessionTimerTick(object? sender, EventArgs e) => RefreshSessionTimer();

    // While paused the text holds still: the pause in progress is taken off as it grows.
    internal void RefreshSessionTimer()
    {
        if (!_sessionClock.IsStarted) return;
        StreamTimerText = _sessionClock.Text;
        foreach (var session in RecordingSessions) session.RefreshTimer();
    }

    private static int FpsFromIndex(int index) => index switch { 0 => 60, 2 => 25, _ => 30 };
    private static (int Width, int Height) OutputResolutionFromIndex(int index) =>
        VideoResolution.OutputFromIndex(index);
    private static int AudioSampleRateFromIndex(int index) => index == 1 ? 44_100 : 48_000;
    private static int AudioChannelsFromIndex(int index) => index == 1 ? 1 : 2;
    private static int AudioBitrateFromIndex(int index) => index switch { 0 => 320, 2 => 128, _ => 192 };
    private static RecordingContainer FormatFromIndex(int index) => index switch
    {
        1 => RecordingContainer.Mkv,
        2 => RecordingContainer.WebM,
        _ => RecordingContainer.Mp4
    };
}
