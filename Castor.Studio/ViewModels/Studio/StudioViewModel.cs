using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CastorApplication.Models.Settings;
using CastorApplication.Models.Settings.Providers;
using CastorApplication.Models.Studio;
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
    private DateTime? _sessionStartUtc;

    public ObservableCollection<SceneItemViewModel> Scenes => _workspace.Scenes;
    public bool IsStreaming => _workspace.IsStreaming;
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

    [ObservableProperty] private string _streamTimerText = "00:00:00";
    [ObservableProperty] private bool _isStreamingTransition;
    [ObservableProperty] private string _connectedAccountLabel = "Compte Twitch non connecté";
    [ObservableProperty] private string _recordError = "";
    [ObservableProperty] private string _streamError = "";
    [ObservableProperty] private string _outputInfoText = "";
    [ObservableProperty] private string _recordingOutputDirectory = "";

    public string StreamStatusText => IsStreaming ? "EN DIRECT" : "OFFLINE";
    public IBrush StreamStatusBrush => SolidColorBrush.Parse(IsStreaming ? "#f87171" : "#3c3c4e");
    public IBrush StreamTimerBrush => SolidColorBrush.Parse(IsStreaming || IsRecording ? "#f87171" : "#3c3c4e");
    public string SceneBarStatusText => IsStreaming ? "EN DIRECT" : IsRecording ? "REC" : "Prêt";
    public IBrush SceneBarStatusBrush => SolidColorBrush.Parse(IsStreaming || IsRecording ? "#f87171" : "#34d399");

    internal StudioViewModel(
        StudioWorkspaceViewModel workspace,
        IStudioRuntime runtime,
        IScenePreviewRuntime previewRuntime,
        IRecordingRuntime recordingRuntime,
        IStreamingRuntime streamingRuntime,
        IProviderStore providerStore,
        SettingsService settingsService,
        VideoCanvasResolutionResolver? resolutionResolver = null)
    {
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
    // same as ScenesViewModel's own OnSettingsSaved.
    private void OnSettingsSaved(object? sender, EventArgs e) => RefreshBaseCanvasSize();

    private void RefreshBaseCanvasSize()
    {
        var resolution = _resolutionResolver.Resolve(_settingsService.Load());
        BaseCanvasWidth = resolution.Width;
        BaseCanvasHeight = resolution.Height;
    }

    [RelayCommand]
    private async Task StartStreaming(CancellationToken cancellationToken)
    {
        StreamError = "";
        if (!_streamingRuntime.IsAvailable)
        {
            StreamError = _streamingRuntime.UnavailableMessage;
            return;
        }

        if (IsRecording)
        {
            StreamError = "Arrêtez l'enregistrement avant de lancer le live.";
            return;
        }

        var scene = ActiveScene;
        if (scene == null || !StudioWorkspaceViewModel.HasVideoSource(scene))
        {
            StreamError = "Aucune source vidéo dans la scène active.";
            return;
        }

        var provider = GetConnectedTwitchProvider();
        if (provider == null)
        {
            StreamError = "Compte Twitch déconnecté. Reconnectez-vous dans Paramètres → Comptes.";
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
                StreamError = result.Message;
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
                StreamError = result.Message;
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
        RecordError = "";
        if (IsStreaming)
        {
            RecordError = "Arrêtez le live avant de démarrer l'enregistrement.";
            return;
        }

        if (!_recordingRuntime.IsAvailable)
        {
            RecordError = _recordingRuntime.UnavailableMessage;
            return;
        }

        var scene = ActiveScene;
        if (scene == null || !StudioWorkspaceViewModel.HasVideoSource(scene))
        {
            RecordError = "Aucune source vidéo dans la scène active.";
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
            RecordError = exception.Message;
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
            RecordError = result.Message;
            return;
        }

        _workspace.SetRecordingState(true);
    }

    [RelayCommand]
    private async Task StopRecording(CancellationToken cancellationToken)
    {
        var result = await _recordingRuntime.StopRecordingAsync(cancellationToken);
        if (!result.IsSuccess)
        {
            RecordError = result.Message;
            return;
        }
        _workspace.SetRecordingState(false);
    }

    private void OnRecordingRuntimeStateChanged(object? sender, RecordingStateChangedEventArgs e)
    {
        void ApplyState()
        {
            _workspace.SetRecordingState(e.IsRecording);
            if (!string.IsNullOrWhiteSpace(e.Message)) RecordError = e.Message;
        }

        if (Application.Current == null || Dispatcher.UIThread.CheckAccess()) ApplyState();
        else Dispatcher.UIThread.Post(ApplyState);
    }

    private void OnStreamingRuntimeStateChanged(object? sender, StreamingStateChangedEventArgs e)
    {
        void ApplyState()
        {
            _workspace.SetStreamingState(e.IsStreaming);
            if (!string.IsNullOrWhiteSpace(e.Message)) StreamError = e.Message;
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
        if (!result.IsSuccess) RecordError = result.Message;
    }

    private void ApplyActiveSceneToStreaming()
    {
        if (!_workspace.IsStreaming) return;

        var scene = ActiveScene;
        if (scene == null) return;

        var result = _streamingRuntime.SwitchStreamingScene(scene.Id);
        if (!result.IsSuccess) StreamError = result.Message;
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StudioWorkspaceViewModel.ActiveScene))
        {
            OnPropertyChanged(nameof(ActiveScene));
            NotifyPreviewChanged();
            ApplyActiveSceneToRecording();
            ApplyActiveSceneToStreaming();
        }
        else if (e.PropertyName is nameof(StudioWorkspaceViewModel.IsRecording) or nameof(StudioWorkspaceViewModel.IsStreaming))
        {
            NotifySessionStateChanged();
            if (IsRecording || IsStreaming) StartSessionTimerIfNeeded();
            else ResetSessionTimer();
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
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(StreamStatusText));
        OnPropertyChanged(nameof(StreamStatusBrush));
        OnPropertyChanged(nameof(StreamTimerBrush));
        OnPropertyChanged(nameof(SceneBarStatusText));
        OnPropertyChanged(nameof(SceneBarStatusBrush));
    }

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
    }

    private void StartSessionTimerIfNeeded()
    {
        if (_sessionStartUtc != null) return;
        _sessionStartUtc = DateTime.UtcNow;
        StreamTimerText = "00:00:00";
        _sessionTimer.Start();
    }

    private void ResetSessionTimer()
    {
        _sessionTimer.Stop();
        _sessionStartUtc = null;
        StreamTimerText = "00:00:00";
    }

    private void OnSessionTimerTick(object? sender, EventArgs e)
    {
        if (_sessionStartUtc == null) return;
        var elapsed = DateTime.UtcNow - _sessionStartUtc.Value;
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
