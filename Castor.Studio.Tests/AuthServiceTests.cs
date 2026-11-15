using CastorApplication.Models.Auth;
using CastorApplication.Models.Settings.Providers;
using CastorApplication.Services.Auth;
using CastorApplication.Services.Auth.Providers;
using CastorApplication.Services.Auth.Storage;
using CastorApplication.ViewModels.Settings.Sections;

namespace Castor.Studio.Tests;

public sealed class AuthServiceTests
{
    [Fact]
    public async Task A_valid_session_is_returned_without_refreshing()
    {
        var fixture = new Fixture(expiresAt: DateTimeOffset.UtcNow.AddHours(1));

        Assert.Equal("access-1", await fixture.Auth.GetAccessTokenAsync("youtube"));
        Assert.Equal(0, fixture.Provider.Refreshes);
    }

    [Fact]
    public async Task An_expired_session_is_refreshed_and_stored()
    {
        var fixture = new Fixture();

        Assert.Equal("access-2", await fixture.Auth.GetAccessTokenAsync("youtube"));
        Assert.Equal("access-2", (await fixture.Tokens.GetAsync("youtube"))?.AccessToken);
        Assert.True(fixture.Accounts.Get("youtube")?.IsConnected);
    }

    [Fact]
    public async Task A_revoked_refresh_token_disconnects_the_account_instead_of_throwing_again()
    {
        var fixture = new Fixture();
        fixture.Provider.Revoked = true;

        Assert.Null(await fixture.Auth.GetAccessTokenAsync("youtube"));

        Assert.Null(await fixture.Tokens.GetAsync("youtube"));
        var account = fixture.Accounts.Get("youtube");
        Assert.NotNull(account);
        Assert.False(account.IsConnected);
        // Kept so the UI can name the account to reconnect.
        Assert.Equal("Club TV", account.UserName);
        Assert.Equal(1, fixture.Accounts.Changes);

        // The next call finds no session: no new refresh, no exception.
        Assert.Null(await fixture.Auth.GetAccessTokenAsync("youtube"));
        Assert.Equal(1, fixture.Provider.Refreshes);
    }

    [Fact]
    public void Accounts_page_offers_to_reconnect_a_revoked_twitch_account()
    {
        var accounts = new FakeAccounts(new ProviderSettings
        {
            ProviderId = "twitch",
            IsConnected = true,
            UserName = "Castor",
            StreamKey = "live-key"
        });
        var page = new AccountsSettingsViewModel(new Fixture().Auth, accounts);
        Assert.Equal("Connecté en tant que Castor", page.TwitchStatus);
        Assert.Equal("Déconnexion", page.TwitchButtonText);

        var account = accounts.Get("twitch")!;
        account.IsConnected = false;
        accounts.Save(account);

        Assert.Equal("Session expirée pour Castor : reconnectez le compte", page.TwitchStatus);
        Assert.Equal("Connexion", page.TwitchButtonText);
    }

    private sealed class Fixture
    {
        public FakeProvider Provider { get; } = new();
        public InMemoryTokenStore Tokens { get; } = new();
        public FakeAccounts Accounts { get; }
        public AuthService Auth { get; }

        public Fixture(DateTimeOffset? expiresAt = null)
        {
            Accounts = new FakeAccounts(new ProviderSettings
            {
                ProviderId = "youtube",
                IsConnected = true,
                UserName = "Club TV",
                StreamKey = "yt-key"
            });
            Tokens.SaveAsync(new AuthSession
            {
                ProviderId = "youtube",
                AccessToken = "access-1",
                RefreshToken = "refresh-1",
                ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(-5)
            }).GetAwaiter().GetResult();
            Accounts.Changes = 0;
            Auth = new AuthService(new ProviderRegistry([Provider]), Tokens, Accounts);
        }
    }

    private sealed class FakeProvider : IAuthProvider
    {
        public bool Revoked { get; set; }
        public int Refreshes { get; private set; }
        public string Id => "youtube";
        public string ClientId => "client-1";

        public Task<AuthSession> RefreshAsync(AuthSession session, CancellationToken ct = default)
        {
            Refreshes++;
            if (Revoked) throw new InvalidGrantException(Id);
            return Task.FromResult(new AuthSession
            {
                ProviderId = Id,
                AccessToken = "access-2",
                RefreshToken = session.RefreshToken,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            });
        }

        public Task<DeviceCodeResult> BeginLoginAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthSession> CompleteLoginAsync(DeviceCodeResult deviceCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RevokeAsync(AuthSession session, CancellationToken ct = default) => Task.CompletedTask;
        public Task<UserProfile> GetProfileAsync(AuthSession session, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeAccounts(params ProviderSettings[] accounts) : IProviderStore
    {
        private readonly List<ProviderSettings> _accounts = [.. accounts];
        public int Changes { get; set; }
        public event EventHandler? Changed;
        public IReadOnlyCollection<ProviderSettings> GetAll() => _accounts;
        public ProviderSettings? Get(string providerId) => _accounts.FirstOrDefault(a => a.ProviderId == providerId);

        public void Save(ProviderSettings provider)
        {
            _accounts.RemoveAll(a => a.ProviderId == provider.ProviderId);
            _accounts.Add(provider);
            Changes++;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Delete(string providerId)
        {
            _accounts.RemoveAll(a => a.ProviderId == providerId);
            Changes++;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
