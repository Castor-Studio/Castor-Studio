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
using CastorApplication.Services.Auth.Providers.YouTube;
using CastorApplication.Services.Auth.Storage;
using CastorApplication.Services.Streaming.YouTube;
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

    public AccountsSettingsViewModel(
        IAuthService authService,
        IProviderStore store,
        IYouTubeLiveService? youTubeLive = null,
        Action<string>? openUrl = null)
    {
        _authService = authService;
        _providerStore = store;
        _youTubeLive = youTubeLive;
        _openUrl = openUrl ?? WebBrowser.Open;
        // A revoked session disconnects its account from a background refresh.
        _providerStore.Changed += OnProviderStoreChanged;

        LoadProviders();
    }

    private void OnProviderStoreChanged(object? sender, EventArgs e)
    {
        if (Application.Current == null || Dispatcher.UIThread.CheckAccess()) RefreshAfterStoreChange();
        else Dispatcher.UIThread.Post(RefreshAfterStoreChange);
    }

    // A login in progress owns its status line; it reloads the providers itself once done.
    private void RefreshAfterStoreChange()
    {
        if (!IsLoading) LoadProviders();
    }

    private void LoadProviders()
    {
        LoadTwitchProvider();
        LoadYouTubeProvider();
    }

    // ── YouTube ──────────────────────────────────────────────────────────────────────────
    // Google's device page does not carry the code: the user types it, so the panel shows it
    // until the consent is given.

    private readonly IYouTubeLiveService? _youTubeLive;
    private readonly Action<string> _openUrl;

    [ObservableProperty]
    private string? _youTubeStatus;

    [ObservableProperty]
    private string _youTubeButtonText = "Connexion";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasYouTubeUserCode))]
    private string _youTubeUserCode = "";

    public bool HasYouTubeUserCode => YouTubeUserCode.Length > 0;

    private bool IsYouTubeConnected => _providerStore.Get(YouTubeAuthProvider.ProviderId) is { IsConnected: true };

    private void LoadYouTubeProvider()
    {
        var provider = _providerStore.Get(YouTubeAuthProvider.ProviderId);
        if (provider is { IsConnected: true })
        {
            YouTubeStatus = $"Connecté à la chaîne {provider.UserName}";
            YouTubeButtonText = "Déconnexion";
        }
        else if (provider != null)
        {
            YouTubeStatus = $"Session expirée pour {provider.UserName} : reconnectez le compte";
            YouTubeButtonText = "Connexion";
        }
        else
        {
            YouTubeStatus = "Non connecté";
            YouTubeButtonText = "Connexion";
        }
    }

    [RelayCommand(CanExecute = nameof(CanToggleTwitch))]
    private async Task ToggleYouTubeAsync()
    {
        if (IsYouTubeConnected)
        {
            await LogoutYouTubeAsync();
            return;
        }

        await ConnectYouTubeAsync();
    }

    private async Task ConnectYouTubeAsync()
    {
        const string providerId = YouTubeAuthProvider.ProviderId;
        try
        {
            IsLoading = true;
            YouTubeStatus = "Demande du code YouTube...";

            var device = await _authService.BeginLoginAsync(providerId);
            YouTubeUserCode = device.UserCode;
            _openUrl(device.VerificationUri);
            YouTubeStatus = "Saisissez ce code sur google.com/device, puis autorisez Castor Studio.";

            var session = await _authService.CompleteLoginAsync(providerId, device);
            YouTubeUserCode = "";
            YouTubeStatus = "Récupération du flux YouTube...";

            YouTubeLiveStream stream;
            try
            {
                if (_youTubeLive == null)
                    throw new InvalidOperationException("Le service YouTube Live est indisponible.");
                stream = await _youTubeLive.ConnectAsync(_providerStore.Get(providerId)?.StreamId, default);
            }
            catch
            {
                // A channel that cannot go live must not be left half connected.
                await _authService.LogoutAsync(providerId);
                throw;
            }

            _providerStore.Save(new ProviderSettings
            {
                ProviderId = providerId,
                IsConnected = true,
                UserId = session.Profile?.Id,
                UserName = session.Profile?.DisplayName,
                StreamKey = stream.StreamKey,
                IngestUrl = stream.IngestUrl,
                StreamId = stream.Id
            });

            LoadYouTubeProvider();
        }
        catch (Exception ex)
        {
            YouTubeUserCode = "";
            YouTubeStatus = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LogoutYouTubeAsync()
    {
        try
        {
            IsLoading = true;
            YouTubeStatus = "Déconnexion de YouTube...";

            await _authService.LogoutAsync(YouTubeAuthProvider.ProviderId);

            _providerStore.Delete(YouTubeAuthProvider.ProviderId);
            LoadYouTubeProvider();
        }
        catch (Exception ex)
        {
            YouTubeStatus = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Twitch ───────────────────────────────────────────────────────────────────────────

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

    // One login at a time: both buttons wait for the running one.
    partial void OnIsLoadingChanged(bool value)
    {
        ToggleTwitchCommand.NotifyCanExecuteChanged();
        ToggleYouTubeCommand.NotifyCanExecuteChanged();
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

            _openUrl(device.VerificationUri);

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
