using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CastorApplication.Models.Settings;
using CastorApplication.Models.Settings.Providers;
using CastorApplication.Services;
using CastorApplication.Services.Auth;
using CastorApplication.Services.Auth.Storage;
using TwitchLib.Api;

namespace CastorApplication.ViewModels.Settings.Sections;

public partial class AccountsSettingsViewModel : SettingsSectionViewModel
{
    private readonly IAuthService _authService;
    private readonly IProviderStore _providerStore;

    [ObservableProperty]
    private string? _twitchStatus;

    [ObservableProperty]
    private string? _twitchButtonText = "Connexion";

    [ObservableProperty]
    private bool _isLoading;

    // An account whose session was revoked stays listed, but disconnected.
    private bool IsTwitchConnected => _providerStore.Get("twitch") is { IsConnected: true };

    public AccountsSettingsViewModel(IAuthService authService, IProviderStore store)
    {
        _authService = authService;
        _providerStore = store;
        // A revoked session disconnects its account from a background refresh.
        _providerStore.Changed += OnProviderStoreChanged;

        LoadTwitchProvider();
    }

    private void OnProviderStoreChanged(object? sender, EventArgs e)
    {
        if (Application.Current == null || Dispatcher.UIThread.CheckAccess()) LoadTwitchProvider();
        else Dispatcher.UIThread.Post(LoadTwitchProvider);
    }

    private void LoadTwitchProvider()
    {
        var provider = _providerStore.Get("twitch");
        if (provider is { IsConnected: true })
        {
            TwitchStatus = $"Connecté en tant que {provider.UserName}";
            TwitchButtonText = "Déconnexion";
        }
        else if (provider != null)
        {
            TwitchStatus = $"Session expirée pour {provider.UserName} : reconnectez le compte";
            TwitchButtonText = "Connexion";
        }
        else
        {
            TwitchStatus = "Non connecté";
            TwitchButtonText = "Connexion";
        }
    }

    private bool CanToggleTwitch()
    {
        return !IsLoading;
    }

    partial void OnIsLoadingChanged(bool value)
    {
        ToggleTwitchCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanToggleTwitch))]
    private async Task ToggleTwitchAsync()
    {
        if (IsTwitchConnected)
        {
            await LogoutTwitchAsync();
            return;
        }

        await ConnectTwitchAsync();
    }

    private async Task ConnectTwitchAsync()
    {
        try
        {
            IsLoading = true;

            TwitchStatus = "Demande du code Twitch...";

            var device =
                await _authService.BeginLoginAsync("twitch");

            WebBrowser.Open(device.VerificationUri);

            TwitchStatus =
                "En attente de l'autorisation Twitch...";

            var session =
                await _authService.CompleteLoginAsync("twitch", device);

            var api = new TwitchAPI();
            api.Settings.ClientId = _authService.GetClientId("twitch");
            api.Settings.AccessToken = session.AccessToken;

            var usersReponse = await api.Helix.Users.GetUsersAsync();
            var user = usersReponse.Users.FirstOrDefault()
                ?? throw new InvalidOperationException("Impossible de récupérer le profil Twitch.");

            var streamKeyResponse = await api.Helix.Streams.GetStreamKeyAsync(user.Id);
            var stream = streamKeyResponse.Streams.FirstOrDefault();
            var streamKey = stream?.Key;
            if (string.IsNullOrWhiteSpace(streamKey))
                throw new InvalidOperationException(
                    "Twitch n'a pas retourné de clé de stream. Vérifiez les autorisations du compte.");

            var providerSetting = new ProviderSettings
            {
                ProviderId = "twitch",
                IsConnected = true,
                UserId = user.Id,
                UserName = user.DisplayName,
                StreamKey = streamKey,
            };

            _providerStore.Save(providerSetting);

            LoadTwitchProvider();
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

    private async Task LogoutTwitchAsync()
    {
        try
        {
            IsLoading = true;
            TwitchStatus = "Déconnexion de Twitch...";

            await _authService.LogoutAsync("twitch");

            _providerStore.Delete("twitch");
            LoadTwitchProvider();
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

    protected override void LoadCore(ApplicationSettings settings)
    {
    }

    protected override void SaveCore(ApplicationSettings settings)
    {
    }
}
