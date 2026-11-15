using CastorApplication.Models.Settings;
using CastorApplication.Models.Settings.Providers;
using CastorApplication.Services.Streaming;
using CastorApplication.Services.Streaming.Twitch;
using CastorApplication.Services.Streaming.YouTube;

namespace Castor.Studio.Tests;

public sealed class LiveBroadcastServiceTests
{
    [Fact]
    public async Task Twitch_needs_no_broadcast_but_takes_the_live_title()
    {
        var youTube = new FakeYouTube();
        var twitch = new FakeTwitch();
        var service = new LiveBroadcastService(youTube, twitch);

        var prepared = await service.PrepareAsync(
            Account("twitch"), new ApplicationSettings { LiveTitle = "  Match du samedi  " }, CancellationToken.None);

        Assert.True(prepared.Result.IsSuccess);
        Assert.Equal("", prepared.Result.Message);
        Assert.Null(prepared.Broadcast);
        Assert.Empty(youTube.Started);
        Assert.Equal([("user-1", "Match du samedi")], twitch.Titles);
    }

    [Fact]
    public async Task A_twitch_title_refused_still_lets_the_live_start_with_a_warning()
    {
        var twitch = new FakeTwitch { Failure = new TwitchApiException(TwitchChannelService.ReconnectMessage) };

        var prepared = await new LiveBroadcastService(new FakeYouTube(), twitch).PrepareAsync(
            Account("twitch"), new ApplicationSettings(), CancellationToken.None);

        Assert.True(prepared.Result.IsSuccess);
        Assert.Equal($"Twitch : titre non modifié. {TwitchChannelService.ReconnectMessage}", prepared.Result.Message);
    }

    [Fact]
    public async Task A_twitch_account_without_its_id_is_asked_to_reconnect()
    {
        var twitch = new FakeTwitch();
        var account = Account("twitch");
        account.UserId = null;

        var prepared = await new LiveBroadcastService(new FakeYouTube(), twitch).PrepareAsync(
            account, new ApplicationSettings(), CancellationToken.None);

        Assert.True(prepared.Result.IsSuccess);
        Assert.Contains("Reconnectez Twitch", prepared.Result.Message);
        Assert.Empty(twitch.Titles);
    }

    [Theory]
    [InlineData("", "Live Castor Studio")]
    [InlineData("  <Finale>  ", "Finale")]
    public void Live_title_keeps_what_youtube_accepts(string title, string expected)
    {
        Assert.Equal(expected, ApplicationSettings.NormalizeLiveTitle(title));
    }

    [Fact]
    public void Live_description_may_stay_empty_and_is_cut_at_youtube_s_limit()
    {
        Assert.Equal("", ApplicationSettings.NormalizeLiveDescription("   "));
        Assert.Equal(5000, ApplicationSettings.NormalizeLiveDescription(new string('a', 6000)).Length);
    }

    [Fact]
    public async Task Youtube_broadcast_uses_the_stream_title_and_visibility()
    {
        var youTube = new FakeYouTube();
        var service = new LiveBroadcastService(youTube, new FakeTwitch());

        var prepared = await service.PrepareAsync(
            Account("youtube", "stream-1"),
            new ApplicationSettings
            {
                LiveTitle = "Match du samedi",
                LiveDescription = "Demi-finale <en direct>",
                YouTubePrivacy = YouTubePrivacy.Unlisted
            },
            CancellationToken.None);

        Assert.True(prepared.Result.IsSuccess);
        Assert.Equal(new LiveBroadcast("youtube", "b1", "https://www.youtube.com/watch?v=b1"), prepared.Broadcast);
        Assert.Equal([("stream-1", "Match du samedi", "Demi-finale en direct", YouTubePrivacy.Unlisted)], youTube.Started);
    }

    [Fact]
    public async Task A_youtube_account_without_its_stream_must_reconnect()
    {
        var youTube = new FakeYouTube();

        var prepared = await new LiveBroadcastService(youTube, new FakeTwitch()).PrepareAsync(
            Account("youtube"), new ApplicationSettings(), CancellationToken.None);

        Assert.False(prepared.Result.IsSuccess);
        Assert.Contains("Reconnectez", prepared.Result.Message);
        Assert.Empty(youTube.Started);
    }

    [Fact]
    public async Task A_youtube_refusal_becomes_the_start_error()
    {
        var youTube = new FakeYouTube { StartFailure = new YouTubeApiException("quotaExceeded", "Le quota de l'API YouTube est épuisé.") };

        var prepared = await new LiveBroadcastService(youTube, new FakeTwitch()).PrepareAsync(
            Account("youtube", "stream-1"), new ApplicationSettings(), CancellationToken.None);

        Assert.False(prepared.Result.IsSuccess);
        Assert.Equal("Le quota de l'API YouTube est épuisé.", prepared.Result.Message);
    }

    [Fact]
    public async Task Ending_reports_a_youtube_failure_without_throwing()
    {
        var youTube = new FakeYouTube { EndFailure = new HttpRequestException("Réseau indisponible") };
        var service = new LiveBroadcastService(youTube, new FakeTwitch());

        var ended = await service.EndAsync(new LiveBroadcast("youtube", "b1", ""), CancellationToken.None);

        Assert.False(ended.IsSuccess);
        Assert.Contains("YouTube n'a pas pu le terminer", ended.Message);
        Assert.Equal(["b1"], youTube.Ended);
    }

    [Fact]
    public async Task Ending_a_youtube_broadcast_asks_youtube()
    {
        var youTube = new FakeYouTube();

        var ended = await new LiveBroadcastService(youTube, new FakeTwitch()).EndAsync(
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
        StreamId = streamId,
        UserId = "user-1"
    };

    private sealed class FakeTwitch : ITwitchChannelService
    {
        public Exception? Failure { get; set; }
        public List<(string BroadcasterId, string Title)> Titles { get; } = [];

        public Task SetTitleAsync(string broadcasterId, string title, CancellationToken cancellationToken)
        {
            if (Failure != null) return Task.FromException(Failure);
            Titles.Add((broadcasterId, title));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeYouTube : IYouTubeLiveService
    {
        public Exception? StartFailure { get; set; }
        public Exception? EndFailure { get; set; }
        public List<(string StreamId, string Title, string Description, YouTubePrivacy Privacy)> Started { get; } = [];
        public List<string> Ended { get; } = [];

        public Task<YouTubeLiveStream> ConnectAsync(string? knownStreamId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<YouTubeBroadcast> StartBroadcastAsync(
            string streamId,
            string title,
            string description,
            YouTubePrivacy privacy,
            CancellationToken cancellationToken)
        {
            Started.Add((streamId, title, description, privacy));
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
