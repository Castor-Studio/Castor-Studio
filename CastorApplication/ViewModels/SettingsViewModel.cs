using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Styling;
using Castor.Native;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
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

    [ObservableProperty]
    private bool _isPluginsActive;

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

    public SettingsViewModel()
    {
        _selectedThemeIndex = Application.Current?.RequestedThemeVariant == ThemeVariant.Light ? 1 : 0;

        try
        {
            foreach (var src in CastorNative.ListAudioSources())
            {
                if (src.Type is AudioSourceType.LoopbackGlobal or AudioSourceType.LoopbackWindow)
                    LoopbackSources.Add(src.Label);
                else
                    MicrophoneSources.Add(src.Label);
            }
        }
        catch { /* module non chargé ou erreur native, listes restent vides */ }
    }

    public string LibraryVersion { get; } = CastorNative.GetVersion();

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

    /// <summary>Sources loopback (système, fenêtre) détectées par libcastor.</summary>
    public ObservableCollection<string> LoopbackSources { get; } = new();

    /// <summary>Micros (USB, jack, bluetooth, micro caméra) détectés par libcastor.</summary>
    public ObservableCollection<string> MicrophoneSources { get; } = new();

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

    [RelayCommand]
    private void ShowPlugins() { ResetCategories(); IsPluginsActive = true; }

    private void ResetCategories()
    {
        IsGeneralActive = false;
        IsVideoActive = false;
        IsAudioActive = false;
        IsStreamingActive = false;
        IsOutputActive = false;
        IsAccountsActive = false;
        IsPluginsActive = false;
    }

    // ── Platform connection commands ──

    [RelayCommand]
    private void ToggleTwitch() => IsTwitchConnected = !IsTwitchConnected;

    [RelayCommand]
    private void ToggleYoutube() => IsYoutubeConnected = !IsYoutubeConnected;

    [RelayCommand]
    private void ToggleFacebook() => IsFacebookConnected = !IsFacebookConnected;
}
