using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CastorApplication.Services.Auth.Providers.Twitch.DTO
{
    internal sealed class TwitchUserResponse
    {
        [JsonPropertyName("data")]
        public required TwitchUserData[] Data { get; init; }
    }

    internal sealed class TwitchUserData
    {
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("display_name")]
        public required string DisplayName { get; init; }

        [JsonPropertyName("profile_image_url")]
        public string? ProfileImageUrl { get; init; }
    }
}
