using System.Net;
using System.Text;
using CastorApplication.Models.Auth;
using CastorApplication.Models.Settings;
using CastorApplication.Services.Auth;
using CastorApplication.Services.Streaming.YouTube;

namespace Castor.Studio.Tests;

public sealed class YouTubeLiveServiceTests
{
    private const string ListUrl =
        "https://www.googleapis.com/youtube/v3/liveStreams?part=id,snippet,cdn,contentDetails&mine=true&maxResults=50";
    private const string InsertUrl =
        "https://www.googleapis.com/youtube/v3/liveStreams?part=snippet,cdn,contentDetails";

    private const string Broadcasts = "https://www.googleapis.com/youtube/v3/liveBroadcasts";
    private const string ActiveBroadcastsUrl =
        Broadcasts + "?part=id,status,contentDetails&broadcastStatus=active&broadcastType=all&maxResults=50";
    private const string InsertBroadcastUrl = Broadcasts + "?part=snippet,status,contentDetails";
    private const string BindUrl = Broadcasts + "/bind?id=b1&part=id,contentDetails&streamId=stream-1";
    private const string DeleteB1Url = Broadcasts + "?id=b1";
    private const string GetB1Url = Broadcasts + "?part=id,status&id=b1";
    private const string CompleteB1Url = Broadcasts + "/transition?broadcastStatus=complete&id=b1&part=id,status";

    [Fact]
    public async Task Start_creates_a_broadcast_bound_to_the_stream_that_starts_and_stops_by_itself()
    {
        var http = new FakeYouTube();
        http.On(ActiveBroadcastsUrl, HttpStatusCode.OK, """{ "items": [] }""");
        http.On(InsertBroadcastUrl, HttpStatusCode.OK, Broadcast("b1", "created"));
        http.On(BindUrl, HttpStatusCode.OK, Broadcast("b1", "ready", "stream-1"));

        var broadcast = await CreateService(http).StartBroadcastAsync(
            "stream-1", "Match du samedi", "Demi-finale, en direct du gymnase.", YouTubePrivacy.Public, CancellationToken.None);

        Assert.Equal(new YouTubeBroadcast("b1", "https://www.youtube.com/watch?v=b1"), broadcast);
        Assert.Equal(
            ["GET " + ActiveBroadcastsUrl, "POST " + InsertBroadcastUrl, "POST " + BindUrl],
            http.Requests.Select(request => $"{request.Method} {request.Url}"));
        var insert = http.Requests[1].Body;
        Assert.Contains("\"title\":\"Match du samedi\"", insert);
        Assert.Contains("\"description\":\"Demi-finale, en direct du gymnase.\"", insert);
        Assert.Contains("\"privacyStatus\":\"public\"", insert);
        Assert.Contains("\"enableAutoStart\":true", insert);
        Assert.Contains("\"enableAutoStop\":true", insert);
        Assert.Contains("\"scheduledStartTime\":", insert);
    }

    [Theory]
    [InlineData(YouTubePrivacy.Unlisted, "unlisted")]
    [InlineData(YouTubePrivacy.Private, "private")]
    public async Task The_chosen_visibility_reaches_youtube(YouTubePrivacy privacy, string expected)
    {
        var http = new FakeYouTube();
        http.On(ActiveBroadcastsUrl, HttpStatusCode.OK, """{ "items": [] }""");
        http.On(InsertBroadcastUrl, HttpStatusCode.OK, Broadcast("b1", "created"));
        http.On(BindUrl, HttpStatusCode.OK, Broadcast("b1", "ready", "stream-1"));

        await CreateService(http).StartBroadcastAsync("stream-1", "Live", "", privacy, CancellationToken.None);

        Assert.Contains($"\"privacyStatus\":\"{expected}\"", http.Requests[1].Body);
    }

    [Fact]
    public async Task A_broadcast_that_cannot_be_bound_is_deleted()
    {
        var http = new FakeYouTube();
        http.On(ActiveBroadcastsUrl, HttpStatusCode.OK, """{ "items": [] }""");
        http.On(InsertBroadcastUrl, HttpStatusCode.OK, Broadcast("b1", "created"));
        http.On(BindUrl, HttpStatusCode.Forbidden, Error(403, "quotaExceeded", "Quota exceeded."));
        http.On(DeleteB1Url, HttpStatusCode.NoContent, "");

        await Assert.ThrowsAsync<YouTubeApiException>(() => CreateService(http).StartBroadcastAsync(
            "stream-1", "Live", "", YouTubePrivacy.Public, CancellationToken.None));

        Assert.Equal("DELETE " + DeleteB1Url, $"{http.Requests[^1].Method} {http.Requests[^1].Url}");
    }

    [Fact]
    public async Task A_broadcast_left_live_on_the_stream_by_a_crash_is_completed_first()
    {
        var http = new FakeYouTube();
        http.On(ActiveBroadcastsUrl, HttpStatusCode.OK, $$"""
            { "items": [ {{Broadcast("b1", "live", "stream-1")}}, {{Broadcast("other", "live", "obs-stream")}} ] }
            """);
        http.On(CompleteB1Url, HttpStatusCode.OK, Broadcast("b1", "complete", "stream-1"));
        http.On(InsertBroadcastUrl, HttpStatusCode.OK, Broadcast("b2", "created"));
        http.On(Broadcasts + "/bind?id=b2&part=id,contentDetails&streamId=stream-1", HttpStatusCode.OK, Broadcast("b2", "ready", "stream-1"));

        var broadcast = await CreateService(http).StartBroadcastAsync(
            "stream-1", "Live", "", YouTubePrivacy.Public, CancellationToken.None);

        Assert.Equal("b2", broadcast.Id);
        Assert.Contains(http.Requests, request => request.Url == CompleteB1Url);
        // A live from another tool on another stream is not Castor's to end.
        Assert.DoesNotContain(http.Requests, request => request.Url.Contains("id=other"));
    }

    [Theory]
    [InlineData("live", "POST " + CompleteB1Url)]
    [InlineData("liveStarting", "POST " + CompleteB1Url)]
    [InlineData("ready", "DELETE " + DeleteB1Url)]
    [InlineData("testing", "DELETE " + DeleteB1Url)]
    public async Task End_completes_a_live_broadcast_and_deletes_one_that_never_went_live(string status, string expected)
    {
        var http = new FakeYouTube();
        http.On(GetB1Url, HttpStatusCode.OK, $$"""{ "items": [ {{Broadcast("b1", status, "stream-1")}} ] }""");
        http.On(CompleteB1Url, HttpStatusCode.OK, Broadcast("b1", "complete", "stream-1"));
        http.On(DeleteB1Url, HttpStatusCode.NoContent, "");

        await CreateService(http).EndBroadcastAsync("b1", CancellationToken.None);

        Assert.Equal(["GET " + GetB1Url, expected], http.Requests.Select(request => $"{request.Method} {request.Url}"));
    }

    [Fact]
    public async Task End_leaves_alone_a_broadcast_already_completed_by_auto_stop()
    {
        var http = new FakeYouTube();
        http.On(GetB1Url, HttpStatusCode.OK, $$"""{ "items": [ {{Broadcast("b1", "complete", "stream-1")}} ] }""");

        await CreateService(http).EndBroadcastAsync("b1", CancellationToken.None);

        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task End_tolerates_auto_stop_winning_the_race()
    {
        var http = new FakeYouTube();
        http.On(GetB1Url, HttpStatusCode.OK, $$"""{ "items": [ {{Broadcast("b1", "live", "stream-1")}} ] }""");
        http.On(CompleteB1Url, HttpStatusCode.Forbidden, Error(403, "redundantTransition", "Redundant transition"));

        await CreateService(http).EndBroadcastAsync("b1", CancellationToken.None);

        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public async Task A_failed_creation_binds_nothing()
    {
        var http = new FakeYouTube();
        http.On(ActiveBroadcastsUrl, HttpStatusCode.OK, """{ "items": [] }""");
        http.On(InsertBroadcastUrl, HttpStatusCode.Forbidden, Error(403, "quotaExceeded", "Quota exceeded."));

        var error = await Assert.ThrowsAsync<YouTubeApiException>(() => CreateService(http).StartBroadcastAsync(
            "stream-1", "Live", "", YouTubePrivacy.Public, CancellationToken.None));

        Assert.Contains("quota", error.Message);
        Assert.DoesNotContain(http.Requests, request => request.Url.Contains("/bind"));
    }

    private static string Broadcast(string id, string status, string? streamId = null) => $$"""
        { "id": "{{id}}", "status": { "lifeCycleStatus": "{{status}}" }
          {{(streamId == null ? "" : $", \"contentDetails\": {{ \"boundStreamId\": \"{streamId}\" }}")}} }
        """;

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
