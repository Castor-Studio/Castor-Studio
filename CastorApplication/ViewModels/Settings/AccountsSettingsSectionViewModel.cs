using CastorApplication.Models;
using CastorApplication.Services.Auth;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OAuth_WPF.Services;
using System;
using System.Threading.Tasks;
using TwitchLib.Api.Helix.Models.Entitlements;

namespace CastorApplication.ViewModels.Settings;

public partial class AccountsSettingsSectionViewModel : SettingsSectionViewModel
{
    private readonly IAuthService _authService;

    public AccountsSettingsSectionViewModel(IAuthService authService)
    {
        _authService = authService;
    }

    [ObservableProperty]
    private string? _twitchStatus;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isTwitchConnected;

    [ObservableProperty]
    private bool _isYoutubeConnected;

    [ObservableProperty]
    private bool _isFacebookConnected;

    public string TwitchStatusText => IsTwitchConnected ? "Connecté" : "Non connecté";
    public string YoutubeStatusText => IsYoutubeConnected ? "Connecté" : "Non connecté";
    public string FacebookStatusText => IsFacebookConnected ? "Connecté" : "Non connecté";

    public string TwitchButtonText => IsTwitchConnected ? "Déconnecter" : "Connecter";
    public string YoutubeButtonText => IsYoutubeConnected ? "Déconnecter" : "Connecter";
    public string FacebookButtonText => IsFacebookConnected ? "Déconnecter" : "Connecter";

    partial void OnIsTwitchConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(TwitchStatusText));
        OnPropertyChanged(nameof(TwitchButtonText));
    }

    partial void OnIsYoutubeConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(YoutubeStatusText));
        OnPropertyChanged(nameof(YoutubeButtonText));
    }

    partial void OnIsFacebookConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(FacebookStatusText));
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

    [RelayCommand]
    public async Task ConnectTwitchAsync()
    {
        try
        {
            IsLoading = true;

            TwitchStatus = "Requesting device code...";

            var device =
                await _authService.BeginLoginAsync("twitch");

            WebBrowser.Open(device.VerificationUri);

            TwitchStatus =
                "Waiting for Twitch authorization...";

            var session =
                await _authService.CompleteLoginAsync("twitch", device);

            TwitchStatus = "Connected";
        }
        catch (Exception ex)
        {
            TwitchStatus = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        await _authService.LogoutAsync("twitch");

        TwitchStatus = "Disconnected";
    }
}
