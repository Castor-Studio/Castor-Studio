using CastorApplication.Models.Auth;
using CastorApplication.Models.Settings.Providers;
using CastorApplication.Services.Auth;
using CastorApplication.Services.Auth.Storage;
using CastorApplication.Services.Streaming.YouTube;
using CastorApplication.ViewModels.Settings.Sections;

namespace Castor.Studio.Tests;

public sealed class AccountsSettingsViewModelTests
{
    [Fact]
    public async Task Connecting_youtube_shows_the_code_then_saves_the_channel_and_its_stream()
    {
        var fixture = new Fixture();
        string? codeWhileWaiting = null;
        fixture.Auth.OnComplete = () => codeWhileWaiting = fixture.Page.YouTubeUserCode;

        await fixture.Page.ToggleYouTubeCommand.ExecuteAsync(null);

        Assert.Equal("ABCD-EFGH", codeWhileWaiting);
        Assert.Equal(["https://www.google.com/device"], fixture.OpenedUrls);
        var account = fixture.Accounts.Get("youtube");
        Assert.NotNull(account);
        Assert.True(account.IsConnected);
        Assert.Equal("UC123", account.UserId);
        Assert.Equal("Club TV", account.UserName);
        Assert.Equal("yt-key", account.StreamKey);
        Assert.Equal("rtmps://a.rtmps.youtube.com:443/live2", account.IngestUrl);
        Assert.Equal("stream-1", account.StreamId);
        Assert.Equal("Connecté à la chaîne Club TV", fixture.Page.YouTubeStatus);
        Assert.Equal("Déconnexion", fixture.Page.YouTubeButtonText);
        Assert.False(fixture.Page.HasYouTubeUserCode);
        Assert.Empty(fixture.Auth.LoggedOut);
    }

    [Fact]
    public async Task A_reconnection_asks_for_the_stream_stored_before()
    {
        var fixture = new Fixture(new ProviderSettings
        {
            ProviderId = "youtube",
            IsConnected = false,
            UserName = "Club TV",
            StreamId = "stream-1"
        });
        Assert.Equal("Session expirée pour Club TV : reconnectez le compte", fixture.Page.YouTubeStatus);

        await fixture.Page.ToggleYouTubeCommand.ExecuteAsync(null);

        Assert.Equal(["stream-1"], fixture.Live.RequestedStreamIds);
        Assert.True(fixture.Accounts.Get("youtube")?.IsConnected);
    }

    [Fact]
    public async Task A_channel_that_cannot_go_live_is_not_saved_and_its_session_is_revoked()
    {
        var fixture = new Fixture();
        fixture.Live.Failure = new YouTubeApiException(
            "liveStreamingNotEnabled",
            "Le direct n'est pas activé sur cette chaîne YouTube.");

        await fixture.Page.ToggleYouTubeCommand.ExecuteAsync(null);

        Assert.Null(fixture.Accounts.Get("youtube"));
        Assert.Equal(["youtube"], fixture.Auth.LoggedOut);
        Assert.Equal("Le direct n'est pas activé sur cette chaîne YouTube.", fixture.Page.YouTubeStatus);
        Assert.Equal("Connexion", fixture.Page.YouTubeButtonText);
        Assert.False(fixture.Page.HasYouTubeUserCode);
    }

    [Fact]
    public async Task Disconnecting_youtube_revokes_the_session_and_forgets_the_account()
    {
        var fixture = new Fixture(new ProviderSettings
        {
            ProviderId = "youtube",
            IsConnected = true,
            UserName = "Club TV",
            StreamKey = "yt-key",
            IngestUrl = "rtmps://a.rtmps.youtube.com:443/live2",
            StreamId = "stream-1"
        });

        await fixture.Page.ToggleYouTubeCommand.ExecuteAsync(null);

        Assert.Equal(["youtube"], fixture.Auth.LoggedOut);
        Assert.Null(fixture.Accounts.Get("youtube"));
        Assert.Equal("Non connecté", fixture.Page.YouTubeStatus);
    }

    [Fact]
    public async Task A_refused_consent_shows_its_message_and_hides_the_code()
    {
        var fixture = new Fixture();
        fixture.Auth.CompleteFailure = new AuthorizationDeniedException("L'autorisation a été refusée sur le compte Google.");

        await fixture.Page.ToggleYouTubeCommand.ExecuteAsync(null);

        Assert.Equal("L'autorisation a été refusée sur le compte Google.", fixture.Page.YouTubeStatus);
        Assert.False(fixture.Page.HasYouTubeUserCode);
        Assert.Null(fixture.Accounts.Get("youtube"));
        Assert.Empty(fixture.Live.RequestedStreamIds);
    }

    private sealed class Fixture
    {
        public FakeAuth Auth { get; } = new();
        public FakeLive Live { get; } = new();
        public FakeAccounts Accounts { get; }
        public List<string> OpenedUrls { get; } = [];
        public AccountsSettingsViewModel Page { get; }

        public Fixture(params ProviderSettings[] accounts)
        {
            Accounts = new FakeAccounts(accounts);
            Page = new AccountsSettingsViewModel(Auth, Accounts, Live, OpenedUrls.Add);
        }
    }

    private sealed class FakeAuth : IAuthService
    {
        public Action? OnComplete { get; set; }
        public Exception? CompleteFailure { get; set; }
        public List<string> LoggedOut { get; } = [];

        public Task<DeviceCodeResult> BeginLoginAsync(string providerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DeviceCodeResult
            {
                DeviceCode = "device-1",
                UserCode = "ABCD-EFGH",
                VerificationUri = "https://www.google.com/device",
                ExpiresIn = TimeSpan.FromMinutes(30),
                Interval = TimeSpan.FromSeconds(5)
            });

        public Task<AuthSession> CompleteLoginAsync(string providerId, DeviceCodeResult deviceCode, CancellationToken cancellationToken = default)
        {
            OnComplete?.Invoke();
            if (CompleteFailure != null) return Task.FromException<AuthSession>(CompleteFailure);
            return Task.FromResult(new AuthSession
            {
                ProviderId = providerId,
                AccessToken = "access-1",
                RefreshToken = "refresh-1",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                Profile = new UserProfile { Id = "UC123", DisplayName = "Club TV" }
            });
        }

        public Task LogoutAsync(string providerId, CancellationToken cancellationToken = default)
        {
            LoggedOut.Add(providerId);
            return Task.CompletedTask;
        }

        public Task<AuthSession?> GetSessionAsync(string providerId, CancellationToken cancellationToken = default) => Task.FromResult<AuthSession?>(null);
        public Task<IReadOnlyCollection<AuthSession>> GetSessionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<AuthSession>>([]);
        public Task<string?> GetAccessTokenAsync(string providerId, CancellationToken cancellationToken = default) => Task.FromResult<string?>("access-1");
        public string GetClientId(string providerId) => "client-1";
    }

    private sealed class FakeLive : IYouTubeLiveService
    {
        public Exception? Failure { get; set; }
        public List<string?> RequestedStreamIds { get; } = [];

        public Task<YouTubeLiveStream> ConnectAsync(string? knownStreamId, CancellationToken cancellationToken)
        {
            RequestedStreamIds.Add(knownStreamId);
            return Failure != null
                ? Task.FromException<YouTubeLiveStream>(Failure)
                : Task.FromResult(new YouTubeLiveStream("stream-1", "yt-key", "rtmps://a.rtmps.youtube.com:443/live2"));
        }
    }

    private sealed class FakeAccounts(IEnumerable<ProviderSettings> accounts) : IProviderStore
    {
        private readonly List<ProviderSettings> _accounts = [.. accounts];
        public event EventHandler? Changed;
        public IReadOnlyCollection<ProviderSettings> GetAll() => _accounts;
        public ProviderSettings? Get(string providerId) => _accounts.FirstOrDefault(a => a.ProviderId == providerId);

        public void Save(ProviderSettings provider)
        {
            _accounts.RemoveAll(a => a.ProviderId == provider.ProviderId);
            _accounts.Add(provider);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Delete(string providerId)
        {
            _accounts.RemoveAll(a => a.ProviderId == providerId);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
