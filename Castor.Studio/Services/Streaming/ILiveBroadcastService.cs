using System.Runtime.CompilerServices;
using CastorApplication.Models.Settings;
using CastorApplication.Models.Settings.Providers;
using CastorApplication.Models.Studio;
using CastorApplication.Services.Diagnostics;
using CastorApplication.Services.Streaming.Twitch;
using CastorApplication.Services.Streaming.YouTube;

namespace CastorApplication.Services.Streaming;

// Platform-side event a live session is attached to (a YouTube liveBroadcast).
internal sealed record LiveBroadcast(string ProviderId, string Id, string WatchUrl);

internal sealed record LiveBroadcastResult(StudioRuntimeResult Result, LiveBroadcast? Broadcast = null)
{
    public static LiveBroadcastResult None { get; } = new(StudioRuntimeResult.Success());
}

// What a platform needs around the live output. Twitch only takes the live title: pushing to the
// key is enough to be live. YouTube needs a broadcast created before the output starts and ended
// after.
internal interface ILiveBroadcastService
{
    // Runs before the output starts; a failure must keep the output from starting. A success may
    // still carry a warning in its message (a Twitch title left unchanged).
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

internal sealed class LiveBroadcastService(IYouTubeLiveService youTube, ITwitchChannelService twitch)
    : ILiveBroadcastService
{
    public async Task<LiveBroadcastResult> PrepareAsync(
        ProviderSettings account,
        ApplicationSettings settings,
        CancellationToken cancellationToken)
    {
        var title = ApplicationSettings.NormalizeLiveTitle(settings.LiveTitle);
        if (account.ProviderId == StreamingProviders.Twitch)
            return await RenameTwitchChannelAsync(account, title, cancellationToken);
        if (account.ProviderId != StreamingProviders.YouTube) return LiveBroadcastResult.None;

        if (string.IsNullOrWhiteSpace(account.StreamId))
            return Failure("Le flux YouTube de ce compte est inconnu. Reconnectez-le dans Paramètres → Comptes.");

        try
        {
            var broadcast = await youTube.StartBroadcastAsync(
                account.StreamId,
                title,
                ApplicationSettings.NormalizeLiveDescription(settings.LiveDescription),
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

    // Twitch goes live on the key alone: a title it refuses only leaves the previous one, so the
    // live still starts, with a warning.
    private async Task<LiveBroadcastResult> RenameTwitchChannelAsync(
        ProviderSettings account,
        string title,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(account.UserId))
            return Warning(TwitchChannelService.ReconnectMessage);

        try
        {
            await twitch.SetTitleAsync(account.UserId, title, cancellationToken);
            return LiveBroadcastResult.None;
        }
        catch (Exception exception) when (IsPlatformFailure(exception))
        {
            return Warning(exception.Message);
        }
    }

    private static LiveBroadcastResult Warning(string reason)
    {
        AppLog.Warning($"Twitch title not changed: {reason}");
        return new(new StudioRuntimeResult(StudioRuntimeStatus.Success, $"Twitch : titre non modifié. {reason}"));
    }

    private static LiveBroadcastResult Failure(string message, [CallerMemberName] string operation = "") =>
        new(StudioRuntimeResult.Failure(message, operation));

    private static bool IsPlatformFailure(Exception exception) =>
        exception is YouTubeApiException or TwitchApiException or HttpRequestException or InvalidOperationException;
}
