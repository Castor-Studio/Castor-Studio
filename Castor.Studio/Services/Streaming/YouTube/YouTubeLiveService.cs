using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CastorApplication.Models.Settings;
using CastorApplication.Services.Auth;
using CastorApplication.Services.Auth.Providers.YouTube;
using CastorApplication.Services.Streaming.YouTube.DTO;

namespace CastorApplication.Services.Streaming.YouTube;

// Where Castor pushes a YouTube live: the RTMPS ingest address and the key of a reusable
// liveStream. The equivalent of Twitch's GetStreamKeyAsync, done once at connection.
public sealed record YouTubeLiveStream(string Id, string StreamKey, string IngestUrl)
{
    // The key must never end up in a log, and a record prints every member by default.
    public override string ToString() => $"YouTube stream {Id} ({IngestUrl})";
}

// A YouTube Data API refusal, with Google's reason ("liveStreamingNotEnabled", "quotaExceeded"...)
// and a message the user can act on.
public sealed class YouTubeApiException(string reason, string message) : Exception(message)
{
    public string Reason { get; } = reason;
}

// The event a live session is shown as on the channel, with its public page.
public sealed record YouTubeBroadcast(string Id, string WatchUrl);

public interface IYouTubeLiveService
{
    // Reuses the stream Castor stored or created before, else creates one.
    Task<YouTubeLiveStream> ConnectAsync(string? knownStreamId, CancellationToken cancellationToken);

    // Creates a broadcast bound to the stream, which goes live as soon as YouTube receives data.
    Task<YouTubeBroadcast> StartBroadcastAsync(
        string streamId,
        string title,
        string description,
        YouTubePrivacy privacy,
        CancellationToken cancellationToken);

    // Completes the broadcast if it went live, deletes it if it never did.
    Task EndBroadcastAsync(string broadcastId, CancellationToken cancellationToken);
}

public sealed class YouTubeLiveService(HttpClient http, IAuthService auth) : IYouTubeLiveService
{
    // Name of the stream Castor creates: a reconnection after a logout finds it back by this title.
    public const string StreamTitle = "Castor Studio";

    private const string LiveStreamsEndpoint = "https://www.googleapis.com/youtube/v3/liveStreams";
    private const string LiveBroadcastsEndpoint = "https://www.googleapis.com/youtube/v3/liveBroadcasts";
    private const string WatchUrlPrefix = "https://www.youtube.com/watch?v=";

    private static readonly HashSet<string> QuotaReasons = new(StringComparer.Ordinal)
    {
        "quotaExceeded", "dailyLimitExceeded", "rateLimitExceeded", "userRateLimitExceeded"
    };

    public async Task<YouTubeLiveStream> ConnectAsync(string? knownStreamId, CancellationToken cancellationToken)
    {
        var token = await AccessTokenAsync(cancellationToken);

        // Costs 1 quota unit; creating a stream costs 50, so an existing one always wins.
        var streams = await SendAsync<LiveStreamListResponse>(
            HttpMethod.Get,
            $"{LiveStreamsEndpoint}?part=id,snippet,cdn,contentDetails&mine=true&maxResults=50",
            token,
            body: null,
            "Lecture des flux YouTube impossible",
            cancellationToken);

        var stream = streams.Items.FirstOrDefault(item =>
                         !string.IsNullOrEmpty(knownStreamId) && item.Id == knownStreamId)
                     ?? streams.Items.FirstOrDefault(item =>
                         item.Snippet?.Title == StreamTitle && item.ContentDetails?.IsReusable == true)
                     ?? await InsertStreamAsync(token, cancellationToken);

        return ToLiveStream(stream);
    }

    public async Task<YouTubeBroadcast> StartBroadcastAsync(
        string streamId,
        string title,
        string description,
        YouTubePrivacy privacy,
        CancellationToken cancellationToken)
    {
        var token = await AccessTokenAsync(cancellationToken);

        // A crash left a broadcast live on this stream: the new one could not take it over.
        await EndStaleBroadcastsAsync(streamId, token, cancellationToken);

        var broadcast = await SendAsync<LiveBroadcastResource>(
            HttpMethod.Post,
            $"{LiveBroadcastsEndpoint}?part=snippet,status,contentDetails",
            token,
            new
            {
                snippet = new { title, description, scheduledStartTime = DateTimeOffset.UtcNow.ToString("O") },
                status = new { privacyStatus = PrivacyStatus(privacy) },
                // Live as soon as YouTube receives the stream, over when it stops: no YouTube Studio.
                contentDetails = new { enableAutoStart = true, enableAutoStop = true }
            },
            "Création du live YouTube impossible",
            cancellationToken);

        try
        {
            await SendAsync<LiveBroadcastResource>(
                HttpMethod.Post,
                $"{LiveBroadcastsEndpoint}/bind?id={Uri.EscapeDataString(broadcast.Id)}" +
                $"&part=id,contentDetails&streamId={Uri.EscapeDataString(streamId)}",
                token,
                body: null,
                "Liaison du live YouTube au flux impossible",
                cancellationToken);
        }
        catch
        {
            // An unbound broadcast would stay scheduled on the channel for nothing.
            await TryDeleteBroadcastAsync(broadcast.Id, token);
            throw;
        }

        return new YouTubeBroadcast(broadcast.Id, WatchUrlPrefix + broadcast.Id);
    }

    public async Task EndBroadcastAsync(string broadcastId, CancellationToken cancellationToken)
    {
        var token = await AccessTokenAsync(cancellationToken);

        var found = await SendAsync<LiveBroadcastListResponse>(
            HttpMethod.Get,
            $"{LiveBroadcastsEndpoint}?part=id,status&id={Uri.EscapeDataString(broadcastId)}",
            token,
            body: null,
            "Lecture du live YouTube impossible",
            cancellationToken);

        var broadcast = found.Items.FirstOrDefault();
        if (broadcast != null) await EndAsync(broadcast, token, cancellationToken);
    }

    private async Task EndStaleBroadcastsAsync(string streamId, string token, CancellationToken cancellationToken)
    {
        var active = await SendAsync<LiveBroadcastListResponse>(
            HttpMethod.Get,
            $"{LiveBroadcastsEndpoint}?part=id,status,contentDetails&broadcastStatus=active&broadcastType=all&maxResults=50",
            token,
            body: null,
            "Lecture des lives YouTube impossible",
            cancellationToken);

        foreach (var broadcast in active.Items.Where(item => item.ContentDetails?.BoundStreamId == streamId))
            await EndAsync(broadcast, token, cancellationToken);
    }

    private async Task EndAsync(LiveBroadcastResource broadcast, string token, CancellationToken cancellationToken)
    {
        switch (broadcast.Status?.LifeCycleStatus)
        {
            case "complete" or "revoked":
                // Auto-stop already ended it.
                return;
            case "live" or "liveStarting":
                try
                {
                    await SendAsync<LiveBroadcastResource>(
                        HttpMethod.Post,
                        $"{LiveBroadcastsEndpoint}/transition?broadcastStatus=complete" +
                        $"&id={Uri.EscapeDataString(broadcast.Id)}&part=id,status",
                        token,
                        body: null,
                        "Fin du live YouTube impossible",
                        cancellationToken);
                }
                // Auto-stop got there first.
                catch (YouTubeApiException exception) when (exception.Reason == "redundantTransition")
                {
                }
                // Still starting: it never really went live, so nothing is lost by removing it.
                catch (YouTubeApiException exception) when (exception.Reason == "invalidTransition")
                {
                    await DeleteBroadcastAsync(broadcast.Id, token, cancellationToken);
                }
                return;
            default:
                // Created, ready or testing: never went live, so it is removed rather than left
                // scheduled on the channel.
                await DeleteBroadcastAsync(broadcast.Id, token, cancellationToken);
                return;
        }
    }

    private async Task DeleteBroadcastAsync(string broadcastId, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"{LiveBroadcastsEndpoint}?id={Uri.EscapeDataString(broadcastId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await http.SendAsync(request, cancellationToken);
        // Already gone is the goal reached.
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound) return;
        throw await FailureAsync(response, "Suppression du live YouTube impossible", cancellationToken);
    }

    private async Task TryDeleteBroadcastAsync(string broadcastId, string token)
    {
        try
        {
            await DeleteBroadcastAsync(broadcastId, token, CancellationToken.None);
        }
        catch (Exception exception) when (exception is YouTubeApiException or HttpRequestException)
        {
            // The bind failure is the error worth reporting.
        }
    }

    private static string PrivacyStatus(YouTubePrivacy privacy) => privacy switch
    {
        YouTubePrivacy.Unlisted => "unlisted",
        YouTubePrivacy.Private => "private",
        _ => "public"
    };

    private Task<LiveStreamResource> InsertStreamAsync(string token, CancellationToken cancellationToken) =>
        SendAsync<LiveStreamResource>(
            HttpMethod.Post,
            $"{LiveStreamsEndpoint}?part=snippet,cdn,contentDetails",
            token,
            new
            {
                snippet = new { title = StreamTitle, description = "Flux réutilisable créé par Castor Studio." },
                // Variable: YouTube adapts to whatever resolution and frame rate Castor sends.
                cdn = new { ingestionType = "rtmp", resolution = "variable", frameRate = "variable" },
                // Reusable: every live session binds a new broadcast to this same stream and key.
                contentDetails = new { isReusable = true }
            },
            "Création du flux YouTube impossible",
            cancellationToken);

    private static YouTubeLiveStream ToLiveStream(LiveStreamResource stream)
    {
        var ingestion = stream.Cdn?.IngestionInfo;
        // RTMPS first: the key travels encrypted. The plain RTMP address stays as a fallback.
        var address = FirstFilled(ingestion?.RtmpsIngestionAddress, ingestion?.IngestionAddress);
        if (string.IsNullOrWhiteSpace(ingestion?.StreamName) || address == null)
            throw new InvalidOperationException("YouTube n'a pas fourni l'adresse et la clé du flux.");

        return new YouTubeLiveStream(stream.Id, ingestion.StreamName, address);
    }

    private static string? FirstFilled(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private async Task<string> AccessTokenAsync(CancellationToken cancellationToken) =>
        await auth.GetAccessTokenAsync(YouTubeAuthProvider.ProviderId, cancellationToken)
        ?? throw new YouTubeApiException(
            "sessionMissing",
            "La session YouTube a expiré. Reconnectez le compte dans Paramètres → Comptes.");

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string url,
        string token,
        object? body,
        string context,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body != null) request.Content = JsonContent.Create(body);

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await FailureAsync(response, context, cancellationToken);

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
               ?? throw new InvalidOperationException($"{context} : réponse de YouTube illisible.");
    }

    // Built from Google's status, reason and message only: never from the request, which may
    // carry the stream key.
    private static async Task<YouTubeApiException> FailureAsync(
        HttpResponseMessage response,
        string context,
        CancellationToken cancellationToken)
    {
        GoogleApiError? error = null;
        try
        {
            error = (await response.Content.ReadFromJsonAsync<GoogleApiErrorResponse>(cancellationToken))?.Error;
        }
        catch (JsonException)
        {
        }

        var reason = error?.Errors.Select(item => item.Reason).FirstOrDefault(r => !string.IsNullOrEmpty(r)) ?? "";

        if (reason == "liveStreamingNotEnabled")
            return new YouTubeApiException(reason,
                "Le direct n'est pas activé sur cette chaîne YouTube. Activez-le dans YouTube Studio " +
                "(Créer → Passer au direct) ; l'activation peut prendre jusqu'à 24 h.");

        if (QuotaReasons.Contains(reason))
            return new YouTubeApiException(reason,
                "Le quota de l'API YouTube est épuisé pour aujourd'hui. Réessayez plus tard.");

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return new YouTubeApiException("unauthorized",
                "La session YouTube n'est plus valide. Reconnectez le compte dans Paramètres → Comptes.");

        var detail = string.IsNullOrWhiteSpace(error?.Message) ? "" : $" : {error.Message}";
        return new YouTubeApiException(reason, $"{context} (HTTP {(int)response.StatusCode}){detail}");
    }
}
