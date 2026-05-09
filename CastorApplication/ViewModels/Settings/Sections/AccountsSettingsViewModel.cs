using CastorApplication.Models;
using CastorApplication.Services.Auth;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OAuth_WPF.Services;
using System;
using System.Threading.Tasks;

namespace CastorApplication.ViewModels.Settings.Sections;

public partial class AccountsSettingsViewModel : SettingsSectionViewModel
{
    private readonly IAuthService _authService;

    [ObservableProperty]
    private string? _twitchStatus;

    [ObservableProperty]
    private string? _twitchButtonText = "Connection";

    [ObservableProperty]
    private bool _isLoading;

    public AccountsSettingsViewModel(IAuthService authService)
    {
        _authService = authService;
    }

    protected override void LoadCore(ApplicationSettings settings)
    {
    }

    protected override void SaveCore(ApplicationSettings settings)
    {
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
