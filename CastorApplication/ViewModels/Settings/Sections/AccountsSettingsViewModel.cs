using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CastorApplication.Models.Auth;
using CastorApplication.Models.Settings;
using CastorApplication.Models.Settings.Providers;
using CastorApplication.Services.Auth;
using CastorApplication.Services.Auth.Storage;
using TwitchLib.Api;
using CastorApplication.Services.Auth.Providers.Youtube;

namespace CastorApplication.ViewModels.Settings.Sections;

public partial class AccountsSettingsViewModel : SettingsSectionViewModel
{
    private readonly IAuthService _authService;
    private readonly IAuthSessionService _sessionService;
    private readonly IProviderStore _providerStore;

    private readonly YoutubeApiClient _youtubeApi;

    [ObservableProperty]
    private string? _twitchStatus;

    [ObservableProperty]
    private string? _twitchButtonText = "Connect";

    [ObservableProperty]
    private string? _youtubeStatus;

    [ObservableProperty]
    private string? _youtubeButtonText = "Connect";

    [ObservableProperty]
    private bool _isLoading;

    public AccountsSettingsViewModel(
        IAuthService authService,
        IAuthSessionService sessionService,
        IProviderStore providerStore,
        YoutubeApiClient apiClient)
    {
        _authService = authService;
        _sessionService = sessionService;
        _providerStore = providerStore;
        _youtubeApi = apiClient;

        LoadProviderState(
            "twitch",
            out var twitchStatus,
            out var twitchButton);

        TwitchStatus = twitchStatus;
        TwitchButtonText = twitchButton;

        LoadProviderState(
            "youtube",
            out var youtubeStatus,
            out var youtubeButton);

        YoutubeStatus = youtubeStatus;
        YoutubeButtonText = youtubeButton;
    }

    private void LoadProviderState(string providerId, out string status, out string button)
    {
        var provider =
            _providerStore.Get(providerId);

        if (provider is not null)
        {
            status =
                $"Connected as {provider.UserName}";

            button = "Logout";
        }
        else
        {
            status = "Not connected";
            button = "Connect";
        }
    }

    [RelayCommand]
    public async Task ConnectTwitchAsync()
    {
        try
        {
            IsLoading = true;

            TwitchStatus = "Connecting...";

            var session =
                await _authService
                    .LoginAsync("twitch");

            var provider =
                _sessionService
                    .GetProvider("twitch");

            var api = new TwitchAPI();

            api.Settings.ClientId =
                provider.ClientId;

            api.Settings.AccessToken =
                session.AccessToken;

            var streamKeyResponse =
                await api.Helix.Streams
                    .GetStreamKeyAsync(session.Profile?.Id);

            var stream =
                streamKeyResponse.Streams
                    .FirstOrDefault();

            var providerSettings =
                new ProviderSettings
                {
                    ProviderId = "twitch",
                    UserId = session.Profile?.Id,
                    UserName = session.Profile?.DisplayName,
                    StreamKey = stream?.Key
                };

            _providerStore.Save(providerSettings);

            TwitchStatus =
                $"Connected as {session.Profile?.DisplayName}";

            TwitchButtonText = "Logout";
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
    public async Task LogoutTwitchAsync()
    {
        try
        {
            IsLoading = true;

            await _sessionService
                .LogoutAsync("twitch");

            _providerStore.Delete("twitch");

            TwitchStatus = "Not connected";

            TwitchButtonText = "Connect";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task ConnectYoutubeAsync()
    {
        try
        {
            IsLoading = true;

            YoutubeStatus = "Connecting...";

            var session = await _authService.LoginAsync("youtube");

            var profile = session.Profile;
            if (profile is null)
            {
                throw new Exception("Failed to retrieve user profile.");
            }

            //var streamKey = await _youtubeApi.GetStreamKeyAsync(profile?.Id);

            var providerSettings = new ProviderSettings
            {
                ProviderId = "youtube",
                UserId = profile?.Id,
                UserName = profile?.DisplayName,
                //StreamKey = streamKey
            };

            _providerStore.Save(providerSettings);

            YoutubeStatus =
                $"Connected as {profile?.DisplayName}";

            YoutubeButtonText = "Logout";
        }
        catch (Exception ex)
        {
            YoutubeStatus = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task LogoutYoutubeAsync()
    {
        try
        {
            IsLoading = true;

            await _sessionService
                .LogoutAsync("youtube");

            _providerStore.Delete("youtube");

            YoutubeStatus = "Not connected";

            YoutubeButtonText = "Connect";
        }
        finally
        {
            IsLoading = false;
        }
    }

    protected override void LoadCore(ApplicationSettings settings)
    {
    }

    protected override void SaveCore(ApplicationSettings settings)
    {
    }
}