using System.Net;
using System.Text;
using CastorApplication.Models.Auth;
using CastorApplication.Services.Auth;
using CastorApplication.Services.Streaming.Twitch;

namespace Castor.Studio.Tests;

public sealed class TwitchChannelServiceTests
{
    private const string ChannelUrl = "https://api.twitch.tv/helix/channels?broadcaster_id=user-1";

    [Fact]
    public async Task The_title_is_patched_on_the_channel_with_the_app_and_user_credentials()
    {
        var http = new FakeTwitch(HttpStatusCode.NoContent, "");

        await CreateService(http).SetTitleAsync("user-1", "Match du samedi", CancellationToken.None);

        var request = Assert.Single(http.Requests);
        Assert.Equal("PATCH", request.Method);
        Assert.Equal(ChannelUrl, request.Url);
        Assert.Equal("Bearer access-1", request.Authorization);
        Assert.Equal("client-1", request.ClientId);
        Assert.Equal("""{"title":"Match du samedi"}""", request.Body);
    }

    [Fact]
    public async Task An_account_connected_before_the_scope_was_asked_must_reconnect_without_calling_twitch()
    {
        var http = new FakeTwitch(HttpStatusCode.NoContent, "");
        var service = new TwitchChannelService(
            new HttpClient(http), new FakeAuth(["user:read:email", "channel:read:stream_key"]));

        var error = await Assert.ThrowsAsync<TwitchApiException>(
            () => service.SetTitleAsync("user-1", "Live", CancellationToken.None));

        Assert.Equal(TwitchChannelService.ReconnectMessage, error.Message);
        Assert.Empty(http.Requests);
    }

    // Connected before sessions were kept across runs: the account streams on its key alone.
    [Fact]
    public async Task An_account_without_a_kept_session_must_reconnect_without_calling_twitch()
    {
        var http = new FakeTwitch(HttpStatusCode.NoContent, "");
        var service = new TwitchChannelService(new HttpClient(http), new FakeAuth(null));

        var error = await Assert.ThrowsAsync<TwitchApiException>(
            () => service.SetTitleAsync("user-1", "Live", CancellationToken.None));

        Assert.Equal(TwitchChannelService.ReconnectMessage, error.Message);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task A_refused_token_asks_to_reconnect()
    {
        var http = new FakeTwitch(HttpStatusCode.Unauthorized,
            """{ "error": "Unauthorized", "status": 401, "message": "Missing scope: channel:manage:broadcast" }""");

        var error = await Assert.ThrowsAsync<TwitchApiException>(
            () => CreateService(http).SetTitleAsync("user-1", "Live", CancellationToken.None));

        Assert.Equal(TwitchChannelService.ReconnectMessage, error.Message);
    }

    [Fact]
    public async Task Any_other_refusal_keeps_twitch_s_message()
    {
        var http = new FakeTwitch(HttpStatusCode.BadRequest,
            """{ "error": "Bad Request", "status": 400, "message": "The title is too long." }""");

        var error = await Assert.ThrowsAsync<TwitchApiException>(
            () => CreateService(http).SetTitleAsync("user-1", "Live", CancellationToken.None));

        Assert.Equal("Twitch a refusé le titre (HTTP 400) : The title is too long.", error.Message);
    }

    private static TwitchChannelService CreateService(FakeTwitch http) =>
        new(new HttpClient(http), new FakeAuth(["channel:read:stream_key", TwitchChannelService.ManageBroadcastScope]));

    // Null scopes: no session kept for the account.
    private sealed class FakeAuth(string[]? scopes) : IAuthService
    {
        public Task<AuthSession?> GetSessionAsync(string providerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(scopes == null
                ? null
                : new AuthSession { ProviderId = providerId, AccessToken = "access-1", Scopes = scopes });

        public Task<string?> GetAccessTokenAsync(string providerId, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("access-1");

        public string GetClientId(string providerId) => "client-1";

        public Task<DeviceCodeResult> BeginLoginAsync(string providerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthSession> CompleteLoginAsync(string providerId, DeviceCodeResult deviceCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<AuthSession>> GetSessionsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task LogoutAsync(string providerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed record RecordedRequest(string Method, string Url, string Body, string? Authorization, string? ClientId);

    private sealed class FakeTwitch(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var content = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.Method.Method,
                request.RequestUri!.ToString(),
                content,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("Client-Id", out var values) ? values.Single() : null));
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
