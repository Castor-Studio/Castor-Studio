using System.Net.Http;
using CastorApplication.Models.Settings;
using CastorApplication.Models.Settings.Providers;
using CastorApplication.Models.Studio;
using CastorApplication.Services.Streaming.YouTube;

namespace CastorApplication.Services.Streaming;

// Platform-side event a live session is attached to (a YouTube liveBroadcast).
internal sealed record LiveBroadcast(string ProviderId, string Id, string WatchUrl);

internal sealed record LiveBroadcastResult(StudioRuntimeResult Result, LiveBroadcast? Broadcast = null)
{
    public static LiveBroadcastResult None { get; } = new(StudioRuntimeResult.Success());
}

// What a platform needs around the live output. Twitch needs nothing: pushing to the key is
// enough to be live. YouTube needs a broadcast created before the output starts and ended after.
internal interface ILiveBroadcastService
{
    // Runs before the output starts; a failure must keep the output from starting.
    Task<LiveBroadcastResult> PrepareAsync(
        ProviderSettings account,
        ApplicationSettings settings,
        CancellationToken cancellationToken);

    // Runs once the output is stopped, whether it was asked to or not, and when it failed to start.
    Task<StudioRuntimeResult> EndAsync(LiveBroadcast broadcast, CancellationToken cancellationToken);
}

internal sealed class NoLiveBroadcastService : ILiveBroadcastService
{
    public static NoLiveBroadcastService Instance { get; } = new();

    public Task<LiveBroadcastResult> PrepareAsync(
        ProviderSettings account,
        ApplicationSettings settings,
        CancellationToken cancellationToken) => Task.FromResult(LiveBroadcastResult.None);

    public Task<StudioRuntimeResult> EndAsync(LiveBroadcast broadcast, CancellationToken cancellationToken) =>
        Task.FromResult(StudioRuntimeResult.Success());
}

internal sealed class LiveBroadcastService(IYouTubeLiveService youTube) : ILiveBroadcastService
{
    public async Task<LiveBroadcastResult> PrepareAsync(
        ProviderSettings account,
        ApplicationSettings settings,
        CancellationToken cancellationToken)
    {
        if (account.ProviderId != StreamingProviders.YouTube) return LiveBroadcastResult.None;

        if (string.IsNullOrWhiteSpace(account.StreamId))
            return Failure("Le flux YouTube de ce compte est inconnu. Reconnectez-le dans Paramètres → Comptes.");

        try
        {
            var broadcast = await youTube.StartBroadcastAsync(
                account.StreamId,
                settings.YouTubeBroadcastTitle,
                settings.YouTubePrivacy,
                cancellationToken);
            return new LiveBroadcastResult(
                StudioRuntimeResult.Success(),
                new LiveBroadcast(StreamingProviders.YouTube, broadcast.Id, broadcast.WatchUrl));
        }
        catch (Exception exception) when (IsPlatformFailure(exception))
        {
            return Failure(exception.Message);
        }
    }

    public async Task<StudioRuntimeResult> EndAsync(LiveBroadcast broadcast, CancellationToken cancellationToken)
    {
        if (broadcast.ProviderId != StreamingProviders.YouTube) return StudioRuntimeResult.Success();

        try
        {
            await youTube.EndBroadcastAsync(broadcast.Id, cancellationToken);
            return StudioRuntimeResult.Success();
        }
        catch (Exception exception) when (IsPlatformFailure(exception))
        {
            // The output is already stopped; auto-stop will end the broadcast on YouTube's side.
            return StudioRuntimeResult.Failure(
                $"Le live est arrêté, mais YouTube n'a pas pu le terminer : {exception.Message}");
        }
    }

    private static LiveBroadcastResult Failure(string message) =>
        new(StudioRuntimeResult.Failure(message));

    private static bool IsPlatformFailure(Exception exception) =>
        exception is YouTubeApiException or HttpRequestException or InvalidOperationException;
}
