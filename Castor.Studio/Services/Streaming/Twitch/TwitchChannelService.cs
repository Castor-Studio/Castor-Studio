using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CastorApplication.Models.Studio;
using CastorApplication.Services.Auth;

namespace CastorApplication.Services.Streaming.Twitch;

// A Twitch Helix refusal, with a message the user can act on.
public sealed class TwitchApiException(string message) : Exception(message);

public interface ITwitchChannelService
{
    // Renames the channel's stream: the title viewers see on the live and in the directory.
    Task SetTitleAsync(string broadcasterId, string title, CancellationToken cancellationToken);
}

public sealed class TwitchChannelService(HttpClient http, IAuthService auth) : ITwitchChannelService
{
    // Asked for at connection since the live title is shared: accounts connected before lack it.
    public const string ManageBroadcastScope = "channel:manage:broadcast";

    public const string ReconnectMessage =
        "Reconnectez Twitch dans Paramètres → Comptes pour que Castor puisse changer le titre.";

    private const string ChannelsEndpoint = "https://api.twitch.tv/helix/channels";

    public async Task SetTitleAsync(string broadcasterId, string title, CancellationToken cancellationToken)
    {
        // Checked first: without the scope, Twitch would only answer 401. No session at all is an
        // account connected before sessions were kept across runs: it streams on its key alone.
        var session = await auth.GetSessionAsync(StreamingProviders.Twitch, cancellationToken);
        if (session == null || !session.Scopes.Contains(ManageBroadcastScope, StringComparer.Ordinal))
            throw new TwitchApiException(ReconnectMessage);

        var token = await auth.GetAccessTokenAsync(StreamingProviders.Twitch, cancellationToken)
                    ?? throw new TwitchApiException(ReconnectMessage);

        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"{ChannelsEndpoint}?broadcaster_id={Uri.EscapeDataString(broadcasterId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Client-Id", auth.GetClientId(StreamingProviders.Twitch));
        request.Content = JsonContent.Create(new { title });

        using var response = await http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new TwitchApiException(ReconnectMessage);
        throw await FailureAsync(response, cancellationToken);
    }

    // Built from Twitch's status and message only, never from the request.
    private static async Task<TwitchApiException> FailureAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string? message = null;
        try
        {
            message = (await response.Content.ReadFromJsonAsync<HelixError>(cancellationToken))?.Message;
        }
        catch (JsonException)
        {
        }

        var detail = string.IsNullOrWhiteSpace(message) ? "" : $" : {message}";
        return new TwitchApiException($"Twitch a refusé le titre (HTTP {(int)response.StatusCode}){detail}");
    }

    private sealed record HelixError([property: JsonPropertyName("message")] string? Message);
}
