using CastorApplication.Models.Settings;
using CastorApplication.Models.Settings.Providers;
using CastorApplication.Services.Streaming;
using CastorApplication.Services.Streaming.YouTube;

namespace Castor.Studio.Tests;

public sealed class LiveBroadcastServiceTests
{
    [Fact]
    public async Task Twitch_needs_no_broadcast()
    {
        var youTube = new FakeYouTube();
        var service = new LiveBroadcastService(youTube);

        var prepared = await service.PrepareAsync(Account("twitch"), new ApplicationSettings(), CancellationToken.None);

        Assert.True(prepared.Result.IsSuccess);
        Assert.Null(prepared.Broadcast);
        Assert.Empty(youTube.Started);
    }

    [Fact]
    public async Task Youtube_broadcast_uses_the_stream_title_and_visibility()
    {
        var youTube = new FakeYouTube();
        var service = new LiveBroadcastService(youTube);

        var prepared = await service.PrepareAsync(
            Account("youtube", "stream-1"),
            new ApplicationSettings { YouTubeBroadcastTitle = "Match du samedi", YouTubePrivacy = YouTubePrivacy.Unlisted },
            CancellationToken.None);

        Assert.True(prepared.Result.IsSuccess);
        Assert.Equal(new LiveBroadcast("youtube", "b1", "https://www.youtube.com/watch?v=b1"), prepared.Broadcast);
        Assert.Equal([("stream-1", "Match du samedi", YouTubePrivacy.Unlisted)], youTube.Started);
    }

    [Fact]
    public async Task A_youtube_account_without_its_stream_must_reconnect()
    {
        var youTube = new FakeYouTube();

        var prepared = await new LiveBroadcastService(youTube).PrepareAsync(
            Account("youtube"), new ApplicationSettings(), CancellationToken.None);

        Assert.False(prepared.Result.IsSuccess);
        Assert.Contains("Reconnectez", prepared.Result.Message);
        Assert.Empty(youTube.Started);
    }

    [Fact]
    public async Task A_youtube_refusal_becomes_the_start_error()
    {
        var youTube = new FakeYouTube { StartFailure = new YouTubeApiException("quotaExceeded", "Le quota de l'API YouTube est épuisé.") };

        var prepared = await new LiveBroadcastService(youTube).PrepareAsync(
            Account("youtube", "stream-1"), new ApplicationSettings(), CancellationToken.None);

        Assert.False(prepared.Result.IsSuccess);
        Assert.Equal("Le quota de l'API YouTube est épuisé.", prepared.Result.Message);
    }

    [Fact]
    public async Task Ending_reports_a_youtube_failure_without_throwing()
    {
        var youTube = new FakeYouTube { EndFailure = new HttpRequestException("Réseau indisponible") };
        var service = new LiveBroadcastService(youTube);

        var ended = await service.EndAsync(new LiveBroadcast("youtube", "b1", ""), CancellationToken.None);

        Assert.False(ended.IsSuccess);
        Assert.Contains("YouTube n'a pas pu le terminer", ended.Message);
        Assert.Equal(["b1"], youTube.Ended);
    }

    [Fact]
    public async Task Ending_a_youtube_broadcast_asks_youtube()
    {
        var youTube = new FakeYouTube();

        var ended = await new LiveBroadcastService(youTube).EndAsync(
            new LiveBroadcast("youtube", "b1", ""), CancellationToken.None);

        Assert.True(ended.IsSuccess);
        Assert.Equal(["b1"], youTube.Ended);
    }

    private static ProviderSettings Account(string providerId, string? streamId = null) => new()
    {
        ProviderId = providerId,
        IsConnected = true,
        UserName = "Club TV",
        StreamKey = "key",
        StreamId = streamId
    };

    private sealed class FakeYouTube : IYouTubeLiveService
    {
        public Exception? StartFailure { get; set; }
        public Exception? EndFailure { get; set; }
        public List<(string StreamId, string Title, YouTubePrivacy Privacy)> Started { get; } = [];
        public List<string> Ended { get; } = [];

        public Task<YouTubeLiveStream> ConnectAsync(string? knownStreamId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<YouTubeBroadcast> StartBroadcastAsync(
            string streamId,
            string title,
            YouTubePrivacy privacy,
            CancellationToken cancellationToken)
        {
            Started.Add((streamId, title, privacy));
            return StartFailure != null
                ? Task.FromException<YouTubeBroadcast>(StartFailure)
                : Task.FromResult(new YouTubeBroadcast("b1", "https://www.youtube.com/watch?v=b1"));
        }

        public Task EndBroadcastAsync(string broadcastId, CancellationToken cancellationToken)
        {
            Ended.Add(broadcastId);
            return EndFailure != null ? Task.FromException(EndFailure) : Task.CompletedTask;
        }
    }
}
