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
    // The file of the current (or just ended) recording, which cancelling deletes.
    private string? _recordingOutputPath;
    private DateTimeOffset? _sessionStartUtc;
    // The timer shows recorded time only: finished pauses add up here, and the one in progress
    // runs from _pausedSinceUtc.
    private DateTimeOffset? _pausedSinceUtc;
    private TimeSpan _pausedTotal;
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

    // Passage d'une scène à l'autre sur le live et l'enregistrement. La preview, elle, coupe
    // toujours sec : elle montre la scène active, pas la sortie programme.
    public IReadOnlyList<SceneTransitionOption> TransitionOptions => SceneTransitionOption.All;
    public IReadOnlyList<int> TransitionDurations => SceneTransitionOption.Durations;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTransitionAnimated), nameof(TransitionDurationText))]
    private SceneTransitionOption? _selectedTransition = SceneTransitionOption.For(SceneTransition.Default.Kind);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TransitionDurationStep), nameof(TransitionDurationText))]
    private int _transitionDurationMs = SceneTransition.DefaultDurationMs;

    // Une coupe n'a pas de durée : le curseur de durée se grise.
    public bool IsTransitionAnimated => SelectedTransition?.Kind != SceneTransitionKind.Cut;

    // Le curseur avance de durée prête en durée prête, pas de milliseconde en milliseconde :
    // un glissé n'écrit les settings qu'à chaque cran, et tombe toujours sur une valeur ronde.
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

    // Errors only: they stay until dismissed (closing their flyout) or until the scene or the
    // page changes, so a failure from an earlier attempt is not shown as a current one.
    public StatusMessage RecordError { get; }
    public StatusMessage StreamError { get; }
    [ObservableProperty] private string _outputInfoText = "";
    [ObservableProperty] private string _recordingOutputDirectory = "";

    public string StreamStatusText => IsStreaming ? "EN DIRECT" : "OFFLINE";
    public string SceneBarStatusText => IsStreaming
        ? "En direct"
        : IsRecordingPaused ? "En pause" : IsRecording ? "Enregistrement" : "Prêt";
    public bool IsRecordingPaused => _workspace.RecordingState == RecordingState.Paused;

    // Filled in when a recording starts: its container and encoder decide whether it can pause.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseToolTip))]
    [NotifyCanExecuteChangedFor(nameof(TogglePauseRecordingCommand))]
    private string _pauseUnavailableReason = "";

    public const string PauseShortcutText = "Ctrl+Maj+P";

    public bool CanTogglePauseRecording =>
        PauseUnavailableReason.Length == 0 &&
        _workspace.RecordingState is RecordingState.Recording or RecordingState.Paused;

    public string PauseButtonText => IsRecordingPaused ? "REPRENDRE" : "PAUSE";

    public string PauseToolTip =>PauseUnavailableReason.Length > 0
        ? PauseUnavailableReason
        : IsRecordingPaused
            ? $"Reprendre l'enregistrement dans le même fichier ({PauseShortcutText})"
            : $"Mettre l'enregistrement en pause ({PauseShortcutText})";
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

    // Le type et la durée se rechargent l'un après l'autre : sans ce garde, le premier
    // s'enregistrerait avec l'ancienne valeur du second.
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

    // Le runtime relit la transition à chaque sauvegarde : le prochain changement de scène
    // l'emploie, live en cours compris.
    private void SaveTransition()
    {
        // Une ComboBox qui se vide pousse null : rien de choisi, rien à enregistrer.
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

    [RelayCommand]
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

        var scene = ActiveScene;
        if (scene == null || !StudioWorkspaceViewModel.HasVideoSource(scene))
        {
            RecordError.ShowError("Aucune source vidéo dans la scène active.");
            return;
        }

        var settings = _settingsService.Load();
        var container = FormatFromIndex(settings.SelectedOutputFormatIndex);
        string path;
        try
        {
            path = RecordingOutputPath.Create(settings.OutputPath, container, DateTime.Now);
        }
        catch (Exception exception)
        {
            RecordError.ShowError(exception.Message);
            return;
        }

        RecordingOutputDirectory = Path.GetDirectoryName(path) ?? "";
        var baseResolution = _resolutionResolver.Resolve(settings);
        var (width, height) = OutputResolutionFromIndex(settings.SelectedOutputResolutionIndex);
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
            container), cancellationToken);
        if (!result.IsSuccess)
        {
            RecordError.ShowError(result.Message);
            return;
        }

        _workspace.TrySetRecordingState(RecordingState.Recording);
        PauseUnavailableReason = _recordingRuntime.PauseUnavailableReason;
        _recordingOutputPath = path;
    }

    public bool CanCancelRecording =>
        _cancelRecordingDialog != null &&
        _workspace.RecordingState is RecordingState.Recording or RecordingState.Paused;

    // Throws the take away: stop, then delete the file this recording created - and only that
    // one, whose name RecordingOutputPath picked so that it did not exist beforehand.
    [RelayCommand(CanExecute = nameof(CanCancelRecording))]
    private async Task CancelRecording(CancellationToken cancellationToken)
    {
        if (_cancelRecordingDialog == null || _recordingOutputPath is not { } path) return;
        if (!await _cancelRecordingDialog.ConfirmAsync(path)) return;

        RecordError.Clear();
        // The output may have ended on its own while the dialog was open; there is nothing
        // left to stop then, only the file to delete.
        if (_workspace.RecordingState is not RecordingState.Idle)
        {
            // Returns once the output has stopped and released the file.
            var result = await _recordingRuntime.StopRecordingAsync(cancellationToken);
            if (!result.IsSuccess && _workspace.RecordingState is not RecordingState.Idle)
            {
                // Still running: the file is in use, so it stays, like the recording.
                RecordError.ShowError(result.Message);
                return;
            }
            ApplyRecordingState(RecordingState.Idle);
        }

        _recordingOutputPath = null;
        if (!await RecordingFile.TryDeleteAsync(path))
            RecordError.ShowError(
                "Enregistrement annulé, mais son fichier n'a pas pu être supprimé. Il est peut-être " +
                $"ouvert dans une autre application, ou protégé en écriture. Supprimez-le à la main : {path}");
    }

    [RelayCommand(CanExecute = nameof(CanTogglePauseRecording))]
    private void TogglePauseRecording()
    {
        RecordError.Clear();
        var pausing = _workspace.RecordingState == RecordingState.Recording;
        var result = pausing ? _recordingRuntime.PauseRecording() : _recordingRuntime.ResumeRecording();
        if (!result.IsSuccess)
        {
            // libobs left the output as it was, so the state stays as well.
            RecordError.ShowError(result.Message);
            return;
        }

        // The runtime also reports the change; applying it twice is a no-op.
        _workspace.TrySetRecordingState(pausing ? RecordingState.Paused : RecordingState.Recording);
    }

    [RelayCommand]
    private async Task StopRecording(CancellationToken cancellationToken)
    {
        var result = await _recordingRuntime.StopRecordingAsync(cancellationToken);
        if (!result.IsSuccess)
        {
            RecordError.ShowError(result.Message);
            return;
        }
        ApplyRecordingState(RecordingState.Idle);
    }

    // An output that ends on its own (disk full, encoder error) or a stop whose "stopping"
    // signal was missed reports Idle straight from Recording / Paused; walk through Stopping
    // so the workspace transition rules still hold.
    private void ApplyRecordingState(RecordingState state)
    {
        if (state == RecordingState.Idle &&
            _workspace.RecordingState is RecordingState.Recording or RecordingState.Paused)
            _workspace.TrySetRecordingState(RecordingState.Stopping);

        _workspace.TrySetRecordingState(state);
    }

    private void OnRecordingRuntimeStateChanged(object? sender, RecordingStateChangedEventArgs e)
    {
        void ApplyState()
        {
            ApplyRecordingState(e.State);
            if (!string.IsNullOrWhiteSpace(e.Message)) RecordError.ShowError(e.Message);
        }

        if (Application.Current == null || Dispatcher.UIThread.CheckAccess()) ApplyState();
        else Dispatcher.UIThread.Post(ApplyState);
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
        if (!_workspace.IsRecording) return;

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

            if (_workspace.RecordingState == RecordingState.Idle) PauseUnavailableReason = "";
            TogglePauseRecordingCommand.NotifyCanExecuteChanged();
            CancelRecordingCommand.NotifyCanExecuteChanged();
        }
    }

    private void NotifyPreviewChanged()
    {
        OnPropertyChanged(nameof(PreviewPlaceholderText));
        OnPropertyChanged(nameof(ShowPreviewPlaceholder));
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
        if (_sessionStartUtc != null) return;
        _sessionStartUtc = _timeProvider.GetUtcNow();
        StreamTimerText = "00:00:00";
        _sessionTimer.Start();
    }

    private void TrackRecordingPause()
    {
        var now = _timeProvider.GetUtcNow();
        if (IsRecordingPaused)
        {
            _pausedSinceUtc ??= now;
        }
        else if (_pausedSinceUtc is { } pausedSince)
        {
            _pausedTotal += now - pausedSince;
            _pausedSinceUtc = null;
        }
        RefreshSessionTimer();
    }

    private void ResetSessionTimer()
    {
        _sessionTimer.Stop();
        _sessionStartUtc = null;
        _pausedSinceUtc = null;
        _pausedTotal = TimeSpan.Zero;
        StreamTimerText = "00:00:00";
    }

    private void OnSessionTimerTick(object? sender, EventArgs e) => RefreshSessionTimer();

    // While paused the text holds still: the pause in progress is taken off as it grows.
    internal void RefreshSessionTimer()
    {
        if (_sessionStartUtc == null) return;
        var now = _timeProvider.GetUtcNow();
        var paused = _pausedTotal + (_pausedSinceUtc is { } pausedSince ? now - pausedSince : TimeSpan.Zero);
        var elapsed = now - _sessionStartUtc.Value - paused;
        StreamTimerText = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
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
