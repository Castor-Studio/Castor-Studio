using System.Text.Json.Serialization;

namespace CastorApplication.Services.Auth.Providers.YouTube.DTO;

internal sealed class GoogleDeviceCodeResponse
{
    [JsonPropertyName("device_code")]
    public required string DeviceCode { get; init; }

    [JsonPropertyName("user_code")]
    public required string UserCode { get; init; }

    // Google names it "verification_url", not the RFC 8628 "verification_uri".
    [JsonPropertyName("verification_url")]
    public required string VerificationUrl { get; init; }

    [JsonPropertyName("expires_in")]
    public required int ExpiresIn { get; init; }

    [JsonPropertyName("interval")]
    public int Interval { get; init; } = 5;
}

internal sealed class GoogleTokenResponse
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    // Only sent on the first exchange: a refresh keeps the previous one.
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    [JsonPropertyName("expires_in")]
    public required int ExpiresIn { get; init; }

    // Space-separated, unlike Twitch which sends an array.
    [JsonPropertyName("scope")]
    public string? Scope { get; init; }
}

internal sealed class GoogleOAuthError
{
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; init; }
}

internal sealed class YouTubeChannelListResponse
{
    [JsonPropertyName("items")]
    public YouTubeChannel[] Items { get; init; } = [];
}

internal sealed class YouTubeChannel
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("snippet")]
    public YouTubeChannelSnippet? Snippet { get; init; }
}

internal sealed class YouTubeChannelSnippet
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("thumbnails")]
    public YouTubeThumbnails? Thumbnails { get; init; }
}

internal sealed class YouTubeThumbnails
{
    [JsonPropertyName("default")]
    public YouTubeThumbnail? Default { get; init; }
}

internal sealed class YouTubeThumbnail
{
    [JsonPropertyName("url")]
    public string? Url { get; init; }
}
