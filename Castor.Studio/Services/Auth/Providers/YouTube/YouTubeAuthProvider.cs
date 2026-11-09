using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CastorApplication.Models.Auth;
using CastorApplication.Models.Config;
using CastorApplication.Services.Auth.Providers.YouTube.DTO;
using CastorApplication.Services.Config;

namespace CastorApplication.Services.Auth.Providers.YouTube;

// Google OAuth 2.0 for TV and limited-input devices: the user types a code on google.com/device,
// Castor polls until the consent is given. Same shape as the Twitch login, with Google's quirks:
// the client secret is required, refreshes keep the previous refresh token, and the profile is
// the YouTube channel rather than the Google account.
public sealed class YouTubeAuthProvider : IAuthProvider
{
    public const string ProviderId = "youtube";

    // youtube.force-ssl is refused by the device flow; this scope covers live streams and broadcasts.
    public const string YouTubeScope = "https://www.googleapis.com/auth/youtube";

    private const string DeviceCodeEndpoint = "https://oauth2.googleapis.com/device/code";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";
    private const string ChannelsEndpoint = "https://www.googleapis.com/youtube/v3/channels?part=snippet&mine=true";
    private const string DeviceCodeGrantType = "urn:ietf:params:oauth:grant-type:device_code";

    private static readonly TimeSpan SlowDownStep = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http;
    private readonly ProviderConfig _options;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public string Id => ProviderId;

    public string ClientId => _options.ClientId;

    public YouTubeAuthProvider(HttpClient httpClient, IConfigService configService)
        : this(httpClient, configService, Task.Delay)
    {
    }

    // The delay is swapped in tests, so polling does not wait for real.
    internal YouTubeAuthProvider(
        HttpClient httpClient,
        IConfigService configService,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _http = httpClient;
        // A config without a youtube entry must not break the app start: login reports it instead.
        _options = configService.Config.Providers.TryGetValue(ProviderId, out var options)
            ? options
            : new ProviderConfig();
        _delay = delay;
    }

    public async Task<DeviceCodeResult> BeginLoginAsync(CancellationToken ct = default)
    {
        EnsureConfigured();

        var scopes = _options.Scopes.Length > 0 ? _options.Scopes : [YouTubeScope];
        using var response = await _http.PostAsync(
            DeviceCodeEndpoint,
            Form(("client_id", _options.ClientId), ("scope", string.Join(' ', scopes))),
            ct);

        if (!response.IsSuccessStatusCode)
            throw await FailureAsync(response, "Demande du code YouTube impossible", ct);

        var dto = await response.Content.ReadFromJsonAsync<GoogleDeviceCodeResponse>(ct)
            ?? throw new InvalidOperationException("Réponse de Google illisible.");

        return new DeviceCodeResult
        {
            DeviceCode = dto.DeviceCode,
            UserCode = dto.UserCode,
            VerificationUri = dto.VerificationUrl,
            ExpiresIn = TimeSpan.FromSeconds(dto.ExpiresIn),
            Interval = dto.Interval > 0 ? TimeSpan.FromSeconds(dto.Interval) : DefaultInterval
        };
    }

    public async Task<AuthSession> CompleteLoginAsync(DeviceCodeResult deviceCode, CancellationToken ct = default)
    {
        EnsureConfigured();
        var interval = deviceCode.Interval > TimeSpan.Zero ? deviceCode.Interval : DefaultInterval;

        while (!deviceCode.IsExpired)
        {
            await _delay(interval, ct);

            using var response = await _http.PostAsync(
                TokenEndpoint,
                Form(
                    ("client_id", _options.ClientId),
                    ("client_secret", _options.ClientSecret!),
                    ("device_code", deviceCode.DeviceCode),
                    ("grant_type", DeviceCodeGrantType)),
                ct);

            if (response.IsSuccessStatusCode)
            {
                var token = await ReadTokenAsync(response, ct);
                return await CreateSessionAsync(token, ct);
            }

            var error = await ReadErrorAsync(response, ct);
            switch (error.Error)
            {
                case "authorization_pending":
                    continue;
                case "slow_down":
                    interval += SlowDownStep;
                    continue;
                case "access_denied":
                    throw new AuthorizationDeniedException(
                        "L'autorisation a été refusée sur le compte Google.");
                case "expired_token":
                    throw ExpiredCode();
                default:
                    throw Failure(response, error, "Connexion YouTube impossible");
            }
        }

        throw ExpiredCode();
    }

    public async Task<AuthSession> RefreshAsync(AuthSession session, CancellationToken ct = default)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(session.RefreshToken))
            throw new InvalidOperationException("Aucun refresh token pour la session YouTube.");

        using var response = await _http.PostAsync(
            TokenEndpoint,
            Form(
                ("client_id", _options.ClientId),
                ("client_secret", _options.ClientSecret!),
                ("refresh_token", session.RefreshToken),
                ("grant_type", "refresh_token")),
            ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadErrorAsync(response, ct);
            if (error.Error == "invalid_grant") throw new InvalidGrantException(Id);
            throw Failure(response, error, "Renouvellement de la session YouTube impossible");
        }

        var token = await ReadTokenAsync(response, ct);
        return new AuthSession
        {
            ProviderId = Id,
            AccessToken = token.AccessToken,
            // Google only sends a refresh token on the first exchange.
            RefreshToken = string.IsNullOrWhiteSpace(token.RefreshToken) ? session.RefreshToken : token.RefreshToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn),
            Scopes = token.Scope is null ? session.Scopes : SplitScopes(token.Scope),
            // The channel does not change with the token: no need to spend a quota unit on it.
            Profile = session.Profile
        };
    }

    public async Task RevokeAsync(AuthSession session, CancellationToken ct = default)
    {
        // Revoking the refresh token ends the whole grant, the access tokens included.
        var token = string.IsNullOrWhiteSpace(session.RefreshToken) ? session.AccessToken : session.RefreshToken;

        using var response = await _http.PostAsync(RevokeEndpoint, Form(("token", token)), ct);
        if (response.IsSuccessStatusCode) return;

        var error = await ReadErrorAsync(response, ct);
        // Already revoked (from the Google account page, for instance): the goal is reached.
        if (error.Error == "invalid_token") return;
        throw Failure(response, error, "Révocation de la session YouTube impossible");
    }

    public async Task<UserProfile> GetProfileAsync(AuthSession session, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ChannelsEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw await FailureAsync(response, "Lecture de la chaîne YouTube impossible", ct);

        var channels = await response.Content.ReadFromJsonAsync<YouTubeChannelListResponse>(ct);
        var channel = channels?.Items.FirstOrDefault()
            ?? throw new InvalidOperationException(
                "Ce compte Google n'a pas de chaîne YouTube. Créez-en une sur youtube.com puis réessayez.");

        return new UserProfile
        {
            Id = channel.Id,
            DisplayName = string.IsNullOrWhiteSpace(channel.Snippet?.Title) ? channel.Id : channel.Snippet.Title,
            AvatarUrl = channel.Snippet?.Thumbnails?.Default?.Url
        };
    }

    private async Task<AuthSession> CreateSessionAsync(GoogleTokenResponse token, CancellationToken ct)
    {
        var session = new AuthSession
        {
            ProviderId = Id,
            AccessToken = token.AccessToken,
            RefreshToken = token.RefreshToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn),
            Scopes = SplitScopes(token.Scope)
        };

        session.Profile = await GetProfileAsync(session, ct);
        return session;
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            throw new AuthNotConfiguredException(
                "YouTube n'est pas configuré : le client OAuth Google est manquant " +
                "(variables CASTOR_YOUTUBE_CLIENT_ID et CASTOR_YOUTUBE_CLIENT_SECRET).");
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] fields) =>
        new(fields.Select(field => new KeyValuePair<string, string>(field.Key, field.Value)));

    private static string[] SplitScopes(string? scope) =>
        scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];

    private static async Task<GoogleTokenResponse> ReadTokenAsync(HttpResponseMessage response, CancellationToken ct) =>
        await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(ct)
        ?? throw new InvalidOperationException("Réponse de Google illisible.");

    // OAuth endpoints answer {"error": "code"}, the YouTube API {"error": {...}}: only the first
    // one carries a code worth branching on.
    private static async Task<GoogleOAuthError> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("error", out var error) ||
                error.ValueKind != JsonValueKind.String)
                return new GoogleOAuthError();

            return new GoogleOAuthError
            {
                Error = error.GetString(),
                ErrorDescription = root.TryGetProperty("error_description", out var description) &&
                                   description.ValueKind == JsonValueKind.String
                    ? description.GetString()
                    : null
            };
        }
        catch (JsonException)
        {
            return new GoogleOAuthError();
        }
    }

    private static async Task<InvalidOperationException> FailureAsync(
        HttpResponseMessage response,
        string context,
        CancellationToken ct) =>
        Failure(response, await ReadErrorAsync(response, ct), context);

    // Built from the status and Google's error code only: the request carried tokens and the
    // client secret, none of which may reach a message.
    private static InvalidOperationException Failure(
        HttpResponseMessage response,
        GoogleOAuthError error,
        string context)
    {
        var detail = error.ErrorDescription ?? error.Error;
        return new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
            ? $"{context} (HTTP {(int)response.StatusCode})."
            : $"{context} (HTTP {(int)response.StatusCode}) : {detail}");
    }

    private static TimeoutException ExpiredCode() =>
        new("Le code YouTube a expiré avant l'autorisation. Recommencez la connexion.");
}
