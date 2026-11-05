using System.Net;
using System.Text;
using CastorApplication.Models.Auth;
using CastorApplication.Services.Auth;
using CastorApplication.Services.Streaming.YouTube;

namespace Castor.Studio.Tests;

public sealed class YouTubeLiveServiceTests
{
    private const string ListUrl =
        "https://www.googleapis.com/youtube/v3/liveStreams?part=id,snippet,cdn,contentDetails&mine=true&maxResults=50";
    private const string InsertUrl =
        "https://www.googleapis.com/youtube/v3/liveStreams?part=snippet,cdn,contentDetails";

    [Fact]
    public async Task The_stored_stream_is_reused()
    {
        var http = new FakeYouTube();
        http.On(ListUrl, HttpStatusCode.OK, List(
            Stream("other", "Mon OBS", "other-key"),
            Stream("stream-1", "Castor Studio", "key-1")));

        var stream = await CreateService(http).ConnectAsync("stream-1", CancellationToken.None);

        Assert.Equal(new YouTubeLiveStream("stream-1", "key-1", "rtmps://a.rtmps.youtube.com:443/live2"), stream);
        Assert.Single(http.Requests);
        Assert.Equal("Bearer access-1", http.Requests[0].Authorization);
    }

    [Fact]
    public async Task Without_a_stored_id_the_castor_stream_is_found_back_by_its_title()
    {
        var http = new FakeYouTube();
        http.On(ListUrl, HttpStatusCode.OK, List(
            Stream("other", "Mon OBS", "other-key"),
            Stream("castor", "Castor Studio", "castor-key")));

        var stream = await CreateService(http).ConnectAsync(null, CancellationToken.None);

        Assert.Equal("castor", stream.Id);
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task Without_a_castor_stream_a_reusable_one_is_created()
    {
        var http = new FakeYouTube();
        http.On(ListUrl, HttpStatusCode.OK, List(Stream("other", "Mon OBS", "other-key")));
        http.On(InsertUrl, HttpStatusCode.OK, Stream("new", "Castor Studio", "new-key"));

        var stream = await CreateService(http).ConnectAsync("deleted-stream", CancellationToken.None);

        Assert.Equal("new", stream.Id);
        Assert.Equal("new-key", stream.StreamKey);
        var insert = http.Requests[1];
        Assert.Equal("POST", insert.Method);
        Assert.Contains("\"isReusable\":true", insert.Body);
        Assert.Contains("\"title\":\"Castor Studio\"", insert.Body);
        Assert.Contains("\"ingestionType\":\"rtmp\"", insert.Body);
    }

    [Fact]
    public async Task Plain_rtmp_address_is_used_when_youtube_gives_no_rtmps_one()
    {
        var http = new FakeYouTube();
        http.On(ListUrl, HttpStatusCode.OK, List(Stream("stream-1", "Castor Studio", "key-1", rtmps: false)));

        var stream = await CreateService(http).ConnectAsync("stream-1", CancellationToken.None);

        Assert.Equal("rtmp://a.rtmp.youtube.com/live2", stream.IngestUrl);
    }

    [Fact]
    public async Task A_channel_without_live_streaming_gets_an_explicit_message()
    {
        var http = new FakeYouTube();
        http.On(ListUrl, HttpStatusCode.OK, List());
        http.On(InsertUrl, HttpStatusCode.Forbidden, Error(403, "liveStreamingNotEnabled", "The user is not enabled for live streaming."));

        var error = await Assert.ThrowsAsync<YouTubeApiException>(
            () => CreateService(http).ConnectAsync(null, CancellationToken.None));

        Assert.Equal("liveStreamingNotEnabled", error.Reason);
        Assert.Contains("YouTube Studio", error.Message);
        Assert.Contains("24 h", error.Message);
    }

    [Theory]
    [InlineData("quotaExceeded")]
    [InlineData("rateLimitExceeded")]
    public async Task Quota_errors_get_a_readable_message(string reason)
    {
        var http = new FakeYouTube();
        http.On(ListUrl, HttpStatusCode.Forbidden, Error(403, reason, "Quota exceeded."));

        var error = await Assert.ThrowsAsync<YouTubeApiException>(
            () => CreateService(http).ConnectAsync(null, CancellationToken.None));

        Assert.Contains("quota", error.Message);
    }

    [Fact]
    public async Task Without_a_session_no_request_is_sent()
    {
        var http = new FakeYouTube();

        var error = await Assert.ThrowsAsync<YouTubeApiException>(
            () => new YouTubeLiveService(new HttpClient(http), new FakeAuth(null)).ConnectAsync(null, CancellationToken.None));

        Assert.Contains("Reconnectez", error.Message);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task The_stream_key_never_shows_in_errors_or_in_the_printed_stream()
    {
        var http = new FakeYouTube();
        http.On(ListUrl, HttpStatusCode.OK, List(Stream("stream-1", "Castor Studio", "secret-key")));

        var stream = await CreateService(http).ConnectAsync("stream-1", CancellationToken.None);

        Assert.DoesNotContain("secret-key", stream.ToString());
    }

    private static YouTubeLiveService CreateService(FakeYouTube http) =>
        new(new HttpClient(http), new FakeAuth("access-1"));

    private static string List(params string[] streams) => $$"""{ "items": [ {{string.Join(",", streams)}} ] }""";

    private static string Stream(string id, string title, string key, bool rtmps = true) => $$"""
        { "id": "{{id}}",
          "snippet": { "title": "{{title}}" },
          "cdn": { "ingestionInfo": { "streamName": "{{key}}",
            "ingestionAddress": "rtmp://a.rtmp.youtube.com/live2"
            {{(rtmps ? ", \"rtmpsIngestionAddress\": \"rtmps://a.rtmps.youtube.com:443/live2\"" : "")}} } },
          "contentDetails": { "isReusable": true } }
        """;

    private static string Error(int code, string reason, string message) => $$"""
        { "error": { "code": {{code}}, "message": "{{message}}", "errors": [ { "reason": "{{reason}}" } ] } }
        """;

    private sealed class FakeAuth(string? token) : IAuthService
    {
        public Task<string?> GetAccessTokenAsync(string providerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(providerId == "youtube" ? token : null);

        public Task<DeviceCodeResult> BeginLoginAsync(string providerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthSession> CompleteLoginAsync(string providerId, DeviceCodeResult deviceCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthSession?> GetSessionAsync(string providerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<AuthSession>> GetSessionsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task LogoutAsync(string providerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public string GetClientId(string providerId) => "";
    }

    private sealed record RecordedRequest(string Method, string Url, string Body, string? Authorization);

    private sealed class FakeYouTube : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _responses = [];

        public List<RecordedRequest> Requests { get; } = [];

        public void On(string url, HttpStatusCode status, string body) => _responses[url] = (status, body);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.Method.Method, url, body, request.Headers.Authorization?.ToString()));

            var (status, json) = _responses.TryGetValue(url, out var response)
                ? response
                : (HttpStatusCode.NotFound, Error(404, "notFound", "Not found"));
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
