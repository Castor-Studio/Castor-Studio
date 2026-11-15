using System.Net;
using System.Text;
using CastorApplication.Models.Auth;
using CastorApplication.Models.Config;
using CastorApplication.Services.Auth;
using CastorApplication.Services.Auth.Providers.YouTube;
using CastorApplication.Services.Config;

namespace Castor.Studio.Tests;

public sealed class YouTubeAuthProviderTests
{
    private const string DeviceCodeUrl = "https://oauth2.googleapis.com/device/code";
    private const string TokenUrl = "https://oauth2.googleapis.com/token";
    private const string RevokeUrl = "https://oauth2.googleapis.com/revoke";
    private const string ChannelsUrl = "https://www.googleapis.com/youtube/v3/channels?part=snippet&mine=true";

    private const string ChannelJson = """
        { "items": [ { "id": "UC123", "snippet": { "title": "Club TV",
          "thumbnails": { "default": { "url": "https://yt3.ggpht.com/avatar.jpg" } } } } ] }
        """;

    private const string TokenJson = """
        { "access_token": "access-1", "refresh_token": "refresh-1", "expires_in": 3599,
          "scope": "https://www.googleapis.com/auth/youtube", "token_type": "Bearer" }
        """;

    [Fact]
    public async Task Begin_login_returns_the_code_to_type_and_the_verification_url()
    {
        var http = new FakeGoogle();
        http.On(DeviceCodeUrl, HttpStatusCode.OK, """
            { "device_code": "device-1", "user_code": "ABCD-EFGH",
              "verification_url": "https://www.google.com/device", "expires_in": 1800, "interval": 5 }
            """);
        var provider = CreateProvider(http);

        var code = await provider.BeginLoginAsync();

        Assert.Equal("ABCD-EFGH", code.UserCode);
        Assert.Equal("https://www.google.com/device", code.VerificationUri);
        Assert.Equal(TimeSpan.FromMinutes(30), code.ExpiresIn);
        Assert.Equal(TimeSpan.FromSeconds(5), code.Interval);
        var request = Assert.Single(http.Requests);
        Assert.Contains("client_id=client-1", request.Body);
        Assert.Contains("scope=https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fyoutube", request.Body);
        // The secret is only for the token endpoint.
        Assert.DoesNotContain("client_secret", request.Body);
    }

    [Theory]
    [InlineData("", "secret-1")]
    [InlineData("client-1", null)]
    public async Task Login_without_an_oauth_client_says_youtube_is_not_configured(string clientId, string? secret)
    {
        var http = new FakeGoogle();
        var provider = CreateProvider(http, clientId, secret);

        var error = await Assert.ThrowsAsync<AuthNotConfiguredException>(() => provider.BeginLoginAsync());

        Assert.Contains("CASTOR_YOUTUBE_CLIENT_SECRET", error.Message);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task Complete_login_polls_until_consent_then_loads_the_channel()
    {
        var http = new FakeGoogle();
        http.On(TokenUrl, HttpStatusCode.PreconditionRequired, """{ "error": "authorization_pending" }""");
        http.On(TokenUrl, HttpStatusCode.Forbidden, """{ "error": "slow_down" }""");
        http.On(TokenUrl, HttpStatusCode.OK, TokenJson);
        http.On(ChannelsUrl, HttpStatusCode.OK, ChannelJson);
        var delays = new List<TimeSpan>();
        var provider = CreateProvider(http, delay: delays.Add);

        var session = await provider.CompleteLoginAsync(DeviceCode());

        Assert.Equal([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)], delays);
        Assert.Equal("youtube", session.ProviderId);
        Assert.Equal("access-1", session.AccessToken);
        Assert.Equal("refresh-1", session.RefreshToken);
        Assert.Equal(["https://www.googleapis.com/auth/youtube"], session.Scopes);
        Assert.False(session.IsExpired());
        Assert.Equal("UC123", session.Profile?.Id);
        Assert.Equal("Club TV", session.Profile?.DisplayName);
        Assert.Equal("https://yt3.ggpht.com/avatar.jpg", session.Profile?.AvatarUrl);

        var poll = http.Requests[0];
        Assert.Contains("client_secret=secret-1", poll.Body);
        Assert.Contains("device_code=device-1", poll.Body);
        Assert.Contains("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Adevice_code", poll.Body);
        Assert.Equal("Bearer access-1", http.Requests[^1].Authorization);
    }

    [Fact]
    public async Task Denied_consent_and_expired_code_raise_two_different_errors()
    {
        var denied = new FakeGoogle();
        denied.On(TokenUrl, HttpStatusCode.Forbidden, """{ "error": "access_denied" }""");
        await Assert.ThrowsAsync<AuthorizationDeniedException>(
            () => CreateProvider(denied).CompleteLoginAsync(DeviceCode()));

        var expired = new FakeGoogle();
        expired.On(TokenUrl, HttpStatusCode.BadRequest, """{ "error": "expired_token" }""");
        await Assert.ThrowsAsync<TimeoutException>(
            () => CreateProvider(expired).CompleteLoginAsync(DeviceCode()));
    }

    [Fact]
    public async Task A_code_already_expired_is_not_polled()
    {
        var http = new FakeGoogle();

        await Assert.ThrowsAsync<TimeoutException>(
            () => CreateProvider(http).CompleteLoginAsync(DeviceCode(TimeSpan.Zero)));

        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task Unexpected_errors_never_repeat_the_secret_or_the_codes()
    {
        var http = new FakeGoogle();
        http.On(TokenUrl, HttpStatusCode.BadRequest, """{ "error": "invalid_client", "error_description": "Unauthorized" }""");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateProvider(http).CompleteLoginAsync(DeviceCode()));

        Assert.Equal("Connexion YouTube impossible (HTTP 400) : Unauthorized", error.Message);
        Assert.DoesNotContain("secret-1", error.Message);
        Assert.DoesNotContain("device-1", error.Message);
    }

    [Fact]
    public async Task Account_without_a_channel_fails_with_a_clear_message()
    {
        var http = new FakeGoogle();
        http.On(TokenUrl, HttpStatusCode.OK, TokenJson);
        http.On(ChannelsUrl, HttpStatusCode.OK, """{ "items": [] }""");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateProvider(http).CompleteLoginAsync(DeviceCode()));

        Assert.Contains("pas de chaîne YouTube", error.Message);
    }

    [Fact]
    public async Task Refresh_keeps_the_previous_refresh_token_and_the_channel()
    {
        var http = new FakeGoogle();
        http.On(TokenUrl, HttpStatusCode.OK, """
            { "access_token": "access-2", "expires_in": 3599,
              "scope": "https://www.googleapis.com/auth/youtube", "token_type": "Bearer" }
            """);
        var profile = new UserProfile { Id = "UC123", DisplayName = "Club TV" };

        var refreshed = await CreateProvider(http).RefreshAsync(Session(profile));

        Assert.Equal("access-2", refreshed.AccessToken);
        Assert.Equal("refresh-1", refreshed.RefreshToken);
        Assert.Same(profile, refreshed.Profile);
        Assert.False(refreshed.IsExpired());
        var request = Assert.Single(http.Requests);
        Assert.Contains("grant_type=refresh_token", request.Body);
        Assert.Contains("refresh_token=refresh-1", request.Body);
        Assert.Contains("client_secret=secret-1", request.Body);
    }

    [Fact]
    public async Task Revoked_refresh_token_raises_invalid_grant()
    {
        var http = new FakeGoogle();
        http.On(TokenUrl, HttpStatusCode.BadRequest, """{ "error": "invalid_grant", "error_description": "Token has been expired or revoked." }""");

        var error = await Assert.ThrowsAsync<InvalidGrantException>(
            () => CreateProvider(http).RefreshAsync(Session()));

        Assert.Equal("youtube", error.ProviderId);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token_and_tolerates_one_already_revoked()
    {
        var http = new FakeGoogle();
        http.On(RevokeUrl, HttpStatusCode.OK, "{}");
        http.On(RevokeUrl, HttpStatusCode.BadRequest, """{ "error": "invalid_token" }""");
        var provider = CreateProvider(http);

        await provider.RevokeAsync(Session());
        await provider.RevokeAsync(Session());

        Assert.Equal("token=refresh-1", http.Requests[0].Body);
    }

    private static YouTubeAuthProvider CreateProvider(
        FakeGoogle http,
        string clientId = "client-1",
        string? secret = "secret-1",
        Action<TimeSpan>? delay = null) =>
        new(new HttpClient(http), new FakeConfig(clientId, secret), (interval, _) =>
        {
            delay?.Invoke(interval);
            return Task.CompletedTask;
        });

    private static DeviceCodeResult DeviceCode(TimeSpan? expiresIn = null) => new()
    {
        DeviceCode = "device-1",
        UserCode = "ABCD-EFGH",
        VerificationUri = "https://www.google.com/device",
        ExpiresIn = expiresIn ?? TimeSpan.FromMinutes(30),
        Interval = TimeSpan.FromSeconds(5)
    };

    private static AuthSession Session(UserProfile? profile = null) => new()
    {
        ProviderId = "youtube",
        AccessToken = "access-1",
        RefreshToken = "refresh-1",
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        Scopes = ["https://www.googleapis.com/auth/youtube"],
        Profile = profile
    };

    private sealed class FakeConfig(string clientId, string? secret) : IConfigService
    {
        public AppConfig Config { get; } = new()
        {
            Providers =
            {
                ["youtube"] = new ProviderConfig
                {
                    ClientId = clientId,
                    ClientSecret = secret,
                    Scopes = ["https://www.googleapis.com/auth/youtube"]
                }
            }
        };

        public ProviderConfig GetProviderConfig(string name) => Config.Providers[name];
    }

    private sealed record RecordedRequest(string Url, string Body, string? Authorization);

    // Answers each URL with its queued responses, in order; the last one repeats.
    private sealed class FakeGoogle : HttpMessageHandler
    {
        private readonly Dictionary<string, Queue<(HttpStatusCode Status, string Body)>> _responses = [];

        public List<RecordedRequest> Requests { get; } = [];

        public void On(string url, HttpStatusCode status, string body)
        {
            if (!_responses.TryGetValue(url, out var queue))
                _responses[url] = queue = new Queue<(HttpStatusCode, string)>();
            queue.Enqueue((status, body));
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(url, body, request.Headers.Authorization?.ToString()));

            if (!_responses.TryGetValue(url, out var queue) || queue.Count == 0)
                return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };

            var (status, json) = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }
}
