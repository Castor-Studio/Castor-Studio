using CastorApplication.Models.Settings;
using CastorApplication.Models.Settings.Providers;
using CastorApplication.Services.Auth;
using CastorApplication.Services.Auth.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OAuth_WPF.Services;
using System;
using System.Linq;
using System.Threading.Tasks;
using TwitchLib.Api;

namespace CastorApplication.ViewModels.Settings.Sections;

public partial class AccountsSettingsViewModel : SettingsSectionViewModel
{
    private readonly IAuthService _authService;
    private readonly IProviderStore _providerStore;

    [ObservableProperty]
    private string? _twitchStatus;

    [ObservableProperty]
    private string? _twitchButtonText = "Connection";

    [ObservableProperty]
    private bool _isLoading;

    public AccountsSettingsViewModel(IAuthService authService, IProviderStore store)
    {
        _authService = authService;
        _providerStore = store;
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

            var api = new TwitchAPI();
            api.Settings.ClientId = _authService.GetClientId("twitch");
            api.Settings.AccessToken = session.AccessToken;
            
            var usersReponse = await api.Helix.Users.GetUsersAsync();
            var user = usersReponse.Users.FirstOrDefault();

            var streamKeyResponse = await api.Helix.Streams.GetStreamKeyAsync(user.Id);
            var stream = streamKeyResponse.Streams.FirstOrDefault();
            var streamKey = stream?.Key;

            var providerSetting = new ProviderSettings
            {
                ProviderId = "twitch",
                UserId = user.Id,
                UserName = user.DisplayName,
                StreamKey = streamKey,
            };

            _providerStore.Save(providerSetting);

            TwitchStatus = $"Connected as {user.DisplayName}";
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

        _providerStore.Delete("twitch");

        TwitchStatus = "Disconnected";
    }

    protected override void LoadCore(ApplicationSettings settings)
    {
    }

    protected override void SaveCore(ApplicationSettings settings)
    {
    }
}
