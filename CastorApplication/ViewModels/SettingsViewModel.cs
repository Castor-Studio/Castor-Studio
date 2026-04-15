using Avalonia;
using Avalonia.Styling;
using CastorApplication.Models;
using CastorApplication.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.ComponentModel;

namespace CastorApplication.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private static readonly HashSet<string> NonDirtyProperties = new()
    {
        nameof(IsGeneralActive),
        nameof(IsVideoActive),
        nameof(IsAudioActive),
        nameof(IsStreamingActive),
        nameof(IsOutputActive),
        nameof(IsAccountsActive),
        nameof(HasUnsavedChanges),
        nameof(VideoBitrateDisplay),
        nameof(TwitchStatus),
        nameof(YoutubeStatus),
        nameof(FacebookStatus),
        nameof(TwitchButtonText),
        nameof(YoutubeButtonText),
        nameof(FacebookButtonText)
    };

    private readonly SettingsService _settingsService;
    private bool _isApplyingSettings;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSettingsCommand))]
    private bool _hasUnsavedChanges;

    // ── Category navigation ──

    [ObservableProperty]
    private bool _isGeneralActive = true;

    [ObservableProperty]
    private bool _isVideoActive;

    [ObservableProperty]
    private bool _isAudioActive;

    [ObservableProperty]
    private bool _isStreamingActive;

    [ObservableProperty]
    private bool _isOutputActive;

    [ObservableProperty]
    private bool _isAccountsActive;

    // ── General settings ──

    [ObservableProperty]
    private int _selectedLanguageIndex;

    [ObservableProperty]
    private bool _autoStart;

    [ObservableProperty]
    private int _selectedThemeIndex;

    partial void OnSelectedThemeIndexChanged(int value)
    {
        Application.Current!.RequestedThemeVariant =
            value == 1 ? ThemeVariant.Light : ThemeVariant.Dark;
    }

    public SettingsViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;

        var loadedSettings = _settingsService.Load();
        ApplySettings(loadedSettings);

        // Sync the ComboBox with the currently active theme (no OnChanged triggered via backing field)
        if (loadedSettings.SelectedThemeIndex is < 0 or > 1)
        {
            _selectedThemeIndex = Application.Current?.RequestedThemeVariant == ThemeVariant.Light ? 1 : 0;
        }

        PropertyChanged += OnTrackedPropertyChanged;
        HasUnsavedChanges = false;
    }

    // ── Video settings ──

    [ObservableProperty]
    private int _selectedBaseResolutionIndex = 1; // 1080p default

    [ObservableProperty]
    private int _selectedOutputResolutionIndex;

    [ObservableProperty]
    private int _selectedFpsIndex;

    [ObservableProperty]
    private double _videoBitrate = 6000;

    public string VideoBitrateDisplay => $"{(int)VideoBitrate}";

    partial void OnVideoBitrateChanged(double value)
    {
        OnPropertyChanged(nameof(VideoBitrateDisplay));
    }

    // ── Audio settings ──

    [ObservableProperty]
    private int _selectedSampleRateIndex;

    [ObservableProperty]
    private int _selectedChannelsIndex;

    [ObservableProperty]
    private int _selectedAudioBitrateIndex = 1; // 192 kbps default

    // ── Streaming settings ──

    [ObservableProperty]
    private int _selectedPlatformIndex;

    [ObservableProperty]
    private string _streamKey = "";

    [ObservableProperty]
    private string _rtmpUrl = "";

    [ObservableProperty]
    private int _selectedServerIndex;

    // ── Platform connection status ──

    [ObservableProperty]
    private bool _isTwitchConnected;

    [ObservableProperty]
    private bool _isYoutubeConnected;

    [ObservableProperty]
    private bool _isFacebookConnected;

    public string TwitchStatus => IsTwitchConnected ? "Connecté" : "Non connecté";
    public string YoutubeStatus => IsYoutubeConnected ? "Connecté" : "Non connecté";
    public string FacebookStatus => IsFacebookConnected ? "Connecté" : "Non connecté";

    public string TwitchButtonText => IsTwitchConnected ? "Déconnecter" : "Connecter";
    public string YoutubeButtonText => IsYoutubeConnected ? "Déconnecter" : "Connecter";
    public string FacebookButtonText => IsFacebookConnected ? "Déconnecter" : "Connecter";

    partial void OnIsTwitchConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(TwitchStatus));
        OnPropertyChanged(nameof(TwitchButtonText));
    }

    partial void OnIsYoutubeConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(YoutubeStatus));
        OnPropertyChanged(nameof(YoutubeButtonText));
    }

    partial void OnIsFacebookConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(FacebookStatus));
        OnPropertyChanged(nameof(FacebookButtonText));
    }

    // ── Output settings ──

    [ObservableProperty]
    private int _selectedOutputFormatIndex;

    [ObservableProperty]
    private string _outputPath = "";

    // ── Category commands ──

    [RelayCommand]
    private void ShowGeneral() { ResetCategories(); IsGeneralActive = true; }

    [RelayCommand]
    private void ShowVideo() { ResetCategories(); IsVideoActive = true; }

    [RelayCommand]
    private void ShowAudio() { ResetCategories(); IsAudioActive = true; }

    [RelayCommand]
    private void ShowStreaming() { ResetCategories(); IsStreamingActive = true; }

    [RelayCommand]
    private void ShowOutput() { ResetCategories(); IsOutputActive = true; }

    [RelayCommand]
    private void ShowAccounts() { ResetCategories(); IsAccountsActive = true; }

    private void ResetCategories()
    {
        IsGeneralActive = false;
        IsVideoActive = false;
        IsAudioActive = false;
        IsStreamingActive = false;
        IsOutputActive = false;
        IsAccountsActive = false;
    }

    // ── Platform connection commands ──

    [RelayCommand]
    private void ToggleTwitch() => IsTwitchConnected = !IsTwitchConnected;

    [RelayCommand]
    private void ToggleYoutube() => IsYoutubeConnected = !IsYoutubeConnected;

    [RelayCommand]
    private void ToggleFacebook() => IsFacebookConnected = !IsFacebookConnected;

    private bool CanSaveSettings => HasUnsavedChanges;

    [RelayCommand(CanExecute = nameof(CanSaveSettings))]
    private void SaveSettings()
    {
        _settingsService.Save(BuildCurrentSettings());
        HasUnsavedChanges = false;
    }

    private void ApplySettings(ApplicationSettings settings)
    {
        _isApplyingSettings = true;

        SelectedLanguageIndex = settings.SelectedLanguageIndex;
        AutoStart = settings.AutoStart;
        SelectedThemeIndex = settings.SelectedThemeIndex;

        SelectedBaseResolutionIndex = settings.SelectedBaseResolutionIndex;
        SelectedOutputResolutionIndex = settings.SelectedOutputResolutionIndex;
        SelectedFpsIndex = settings.SelectedFpsIndex;
        VideoBitrate = settings.VideoBitrate;

        SelectedSampleRateIndex = settings.SelectedSampleRateIndex;
        SelectedChannelsIndex = settings.SelectedChannelsIndex;
        SelectedAudioBitrateIndex = settings.SelectedAudioBitrateIndex;

        SelectedPlatformIndex = settings.SelectedPlatformIndex;
        StreamKey = settings.StreamKey;
        RtmpUrl = settings.RtmpUrl;
        SelectedServerIndex = settings.SelectedServerIndex;

        IsTwitchConnected = settings.IsTwitchConnected;
        IsYoutubeConnected = settings.IsYoutubeConnected;
        IsFacebookConnected = settings.IsFacebookConnected;

        SelectedOutputFormatIndex = settings.SelectedOutputFormatIndex;
        OutputPath = settings.OutputPath;

        _isApplyingSettings = false;
    }

    private void OnTrackedPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(e.PropertyName) || NonDirtyProperties.Contains(e.PropertyName))
        {
            return;
        }

        HasUnsavedChanges = true;
    }

    private ApplicationSettings BuildCurrentSettings() => new()
    {
        SelectedLanguageIndex = SelectedLanguageIndex,
        AutoStart = AutoStart,
        SelectedThemeIndex = SelectedThemeIndex,

        SelectedBaseResolutionIndex = SelectedBaseResolutionIndex,
        SelectedOutputResolutionIndex = SelectedOutputResolutionIndex,
        SelectedFpsIndex = SelectedFpsIndex,
        VideoBitrate = VideoBitrate,

        SelectedSampleRateIndex = SelectedSampleRateIndex,
        SelectedChannelsIndex = SelectedChannelsIndex,
        SelectedAudioBitrateIndex = SelectedAudioBitrateIndex,

        SelectedPlatformIndex = SelectedPlatformIndex,
        StreamKey = StreamKey,
        RtmpUrl = RtmpUrl,
        SelectedServerIndex = SelectedServerIndex,

        IsTwitchConnected = IsTwitchConnected,
        IsYoutubeConnected = IsYoutubeConnected,
        IsFacebookConnected = IsFacebookConnected,

        SelectedOutputFormatIndex = SelectedOutputFormatIndex,
        OutputPath = OutputPath
    };
}
