using System.Text.Json.Serialization;

namespace CastorApplication.Services.Auth.Providers.Twitch.DTO
{
    public sealed class TwitchTokenResponse
    {
        [JsonPropertyName("access_token")]
        public required string AccessToken { get; init; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }

        [JsonPropertyName("expires_in")]
        public required int ExpiresIn { get; init; }

        [JsonPropertyName("scope")]
        public string[] Scope { get; init; } = [];

        [JsonPropertyName("token_type")]
        public required string TokenType { get; init; }
    }
}
