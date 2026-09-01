using System.Text.Json.Serialization;

namespace CastorApplication.Services.Auth.Providers.Twitch.DTO
{
    internal sealed class TwitchDeviceCodeResponse
    {
        [JsonPropertyName("device_code")]
        public required string DeviceCode { get; init; }

        [JsonPropertyName("user_code")]
        public required string UserCode { get; init; }

        [JsonPropertyName("verification_uri")]
        public required string VerificationUri { get; init; }

        [JsonPropertyName("expires_in")]
        public required int ExpiresIn { get; init; }

        [JsonPropertyName("interval")]
        public required int Interval { get; init; }
    }
}
