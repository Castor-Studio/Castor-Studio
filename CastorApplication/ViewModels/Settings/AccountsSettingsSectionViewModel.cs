using CastorApplication.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.ViewModels.Settings;

public partial class AccountsSettingsSectionViewModel : SettingsSectionViewModel
{
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

    [RelayCommand]
    private void ToggleTwitch()
    {
        IsTwitchConnected = !IsTwitchConnected;
    }

    [RelayCommand]
    private void ToggleYoutube()
    {
        IsYoutubeConnected = !IsYoutubeConnected;
    }

    [RelayCommand]
    private void ToggleFacebook()
    {
        IsFacebookConnected = !IsFacebookConnected;
    }

    protected override void LoadCore(ApplicationSettings settings)
    {
        IsTwitchConnected = settings.IsTwitchConnected;
        IsYoutubeConnected = settings.IsYoutubeConnected;
        IsFacebookConnected = settings.IsFacebookConnected;
    }

    protected override void SaveCore(ApplicationSettings settings)
    {
        settings.IsTwitchConnected = IsTwitchConnected;
        settings.IsYoutubeConnected = IsYoutubeConnected;
        settings.IsFacebookConnected = IsFacebookConnected;
    }
}
