using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Threading;
using CastorApplication.Models.Studio;
using CastorApplication.Services;
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
    private readonly IProviderStore _providerStore;
    private readonly SettingsService _settingsService;
    private readonly IFilePickerService _filePickerService;
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

    public string PreviewPlaceholderText => !_runtime.IsAvailable
        ? _runtime.UnavailableMessage
        : ActiveScene == null
            ? "Aucune scène active — créez-en une dans l'onglet Scènes."
            : !StudioWorkspaceViewModel.HasVideoSource(ActiveScene)
                ? "Cette scène n'a pas de source vidéo."
                : "";

    public bool ShowPreviewPlaceholder => PreviewPlaceholderText.Length > 0;

    [ObservableProperty] private int _streamPlatformIndex;
    [ObservableProperty] private string _streamRtmpKey = "";
    [ObservableProperty] private string _streamTimerText = "00:00:00";
    [ObservableProperty] private bool _isManualKeyRequired = true;
    [ObservableProperty] private string _connectedAccountLabel = "";
    [ObservableProperty] private string _recordError = "";
    [ObservableProperty] private string _streamError = "";
    [ObservableProperty] private string _outputInfoText = "";

    public string StreamStatusText => IsStreaming ? "EN DIRECT" : "OFFLINE";
    public IBrush StreamStatusBrush => SolidColorBrush.Parse(IsStreaming ? "#f87171" : "#3c3c4e");
    public IBrush StreamTimerBrush => SolidColorBrush.Parse(IsStreaming || IsRecording ? "#f87171" : "#3c3c4e");
    public string SceneBarStatusText => IsStreaming ? "EN DIRECT" : IsRecording ? "REC" : "Prêt";
    public IBrush SceneBarStatusBrush => SolidColorBrush.Parse(IsStreaming || IsRecording ? "#f87171" : "#34d399");

    internal StudioViewModel(
        StudioWorkspaceViewModel workspace,
        IStudioRuntime runtime,
        IProviderStore providerStore,
        SettingsService settingsService,
        IFilePickerService filePickerService)
    {
        _workspace = workspace;
        _runtime = runtime;
        _providerStore = providerStore;
        _settingsService = settingsService;
        _filePickerService = filePickerService;
        _workspace.PropertyChanged += OnWorkspacePropertyChanged;
        _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sessionTimer.Tick += OnSessionTimerTick;
        RefreshProviderState(StreamPlatformIndex);
        RefreshOutputInfo();
    }

    public void RefreshOutputInfo()
    {
        var settings = _settingsService.Load();
        var (width, height) = OutputResolutionFromIndex(settings.SelectedOutputResolutionIndex);
        OutputInfoText = $"{width} × {height} @ {FpsFromIndex(settings.SelectedFpsIndex)} fps";
    }

    public async Task EnsurePreviewRunning(CancellationToken cancellationToken = default)
    {
        RefreshOutputInfo();
        NotifyPreviewChanged();
        if (ActiveScene == null || !StudioWorkspaceViewModel.HasVideoSource(ActiveScene)) return;
        var result = await _runtime.StartPreviewAsync(ActiveScene.ToDefinition(), cancellationToken);
        if (!result.IsSuccess) StreamError = result.Message;
    }

    [RelayCommand]
    private async Task StartStreaming(CancellationToken cancellationToken)
    {
        StreamError = "";
        if (!_runtime.IsAvailable)
        {
            StreamError = _runtime.UnavailableMessage;
            return;
        }

        var scene = ActiveScene;
        if (scene == null || !StudioWorkspaceViewModel.HasVideoSource(scene))
        {
            StreamError = "Aucune source vidéo dans la scène active.";
            return;
        }

        var platform = StreamPlatformIndex switch
        {
            0 => StreamingPlatform.Twitch,
            1 => StreamingPlatform.YouTube,
            _ => StreamingPlatform.Custom
        };
        var keyOrUrl = ResolveStreamDestination();
        if (string.IsNullOrWhiteSpace(keyOrUrl)) return;

        var settings = _settingsService.Load();
        var result = await _runtime.StartStreamingAsync(new StreamingRequest(
            scene.ToDefinition(), platform, keyOrUrl, FpsFromIndex(settings.SelectedFpsIndex), (int)settings.StreamingBitrate), cancellationToken);
        if (!result.IsSuccess)
        {
            StreamError = result.Message;
            return;
        }

        _workspace.SetStreamingState(true);
    }

    [RelayCommand]
    private async Task StopStreaming(CancellationToken cancellationToken)
    {
        var result = await _runtime.StopStreamingAsync(cancellationToken);
        if (!result.IsSuccess)
        {
            StreamError = result.Message;
            return;
        }
        _workspace.SetStreamingState(false);
    }

    [RelayCommand]
    private async Task StartRecording(CancellationToken cancellationToken)
    {
        RecordError = "";
        if (!_runtime.IsAvailable)
        {
            RecordError = _runtime.UnavailableMessage;
            return;
        }

        var scene = ActiveScene;
        if (scene == null || !StudioWorkspaceViewModel.HasVideoSource(scene))
        {
            RecordError = "Aucune source vidéo dans la scène active.";
            return;
        }

        var settings = _settingsService.Load();
        var (extension, label, container) = FormatFromIndex(settings.SelectedOutputFormatIndex);
        var path = await _filePickerService.PickRecordingOutputFileAsync(extension, label);
        if (path == null) return;
        var (width, height) = OutputResolutionFromIndex(settings.SelectedOutputResolutionIndex);
        var result = await _runtime.StartRecordingAsync(new RecordingRequest(
            scene.ToDefinition(), path, FpsFromIndex(settings.SelectedFpsIndex), (int)settings.VideoBitrate,
            width, height, settings.RecordingQualityIndex, container), cancellationToken);
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
        var result = await _runtime.StopRecordingAsync(cancellationToken);
        if (!result.IsSuccess)
        {
            RecordError = result.Message;
            return;
        }
        _workspace.SetRecordingState(false);
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StudioWorkspaceViewModel.ActiveScene))
        {
            OnPropertyChanged(nameof(ActiveScene));
            NotifyPreviewChanged();
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

    private string? ResolveStreamDestination()
    {
        var providerId = GetProviderId(StreamPlatformIndex);
        if (providerId == null)
        {
            if (!string.IsNullOrWhiteSpace(StreamRtmpKey)) return StreamRtmpKey;
            StreamError = "URL RTMP manquante.";
            return null;
        }

        var provider = _providerStore.Get(providerId);
        if (!string.IsNullOrWhiteSpace(provider?.StreamKey)) return provider.StreamKey;
        if (!string.IsNullOrWhiteSpace(StreamRtmpKey)) return StreamRtmpKey;
        StreamError = $"Compte {GetPlatformName(StreamPlatformIndex)} déconnecté. Reconnectez-vous dans Paramètres → Comptes.";
        return null;
    }

    partial void OnStreamPlatformIndexChanged(int value)
    {
        RefreshProviderState(value);
        if (value == 2 && string.IsNullOrWhiteSpace(StreamRtmpKey)) StreamRtmpKey = AppSettings.CustomRtmpUrl;
    }

    private void RefreshProviderState(int platformIndex)
    {
        var providerId = GetProviderId(platformIndex);
        var provider = providerId == null ? null : _providerStore.Get(providerId);
        IsManualKeyRequired = providerId == null || provider == null;
        ConnectedAccountLabel = provider == null ? "" : $"Connecté en tant que {provider.UserName}";
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
    private static (int Width, int Height) OutputResolutionFromIndex(int index) => index switch
    {
        1 => (1280, 720), 2 => (854, 480), _ => (1920, 1080)
    };
    private static (string Extension, string Label, string Container) FormatFromIndex(int index) => index switch
    {
        1 => (".mkv", "MKV", "mkv"), 2 => (".webm", "WebM", "webm"), _ => (".mp4", "MP4", "mp4")
    };
    private static string? GetProviderId(int index) => index switch { 0 => "twitch", 1 => "youtube", _ => null };
    private static string GetPlatformName(int index) => index switch { 0 => "Twitch", 1 => "YouTube Live", _ => "RTMP" };
}
