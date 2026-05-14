using System.Collections.Generic;

namespace CastorApplication.Services.Auth.Providers.Youtube.DTO
{
    public class YoutubeLiveStreamsResponse
    {
        public List<YoutubeLiveStreamItem>? Items { get; set; }
    }

    public class YoutubeLiveStreamItem
    {
        public YoutubeLiveStreamCdn? Cdn { get; set; }
    }

    public class YoutubeLiveStreamCdn
    {
        public YoutubeIngestionInfo? IngestionInfo { get; set; }
    }

    public class YoutubeIngestionInfo
    {
        public string? StreamName { get; set; }
    }
}