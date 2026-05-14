using System.Collections.Generic;

namespace CastorApplication.Services.Auth.Providers.Youtube.DTO
{
    public class YoutubeChannelsResponse
    {
        public List<YoutubeChannelItem> Items { get; set; }
    }

    public class YoutubeChannelItem
    {
        public string Id { get; set; }
        public YoutubeChannelSnippet Snippet { get; set; }
    }

    public class YoutubeChannelSnippet
    {
        public string Title { get; set; }
        public YoutubeThumbnailSet Thumbnails { get; set; }
    }

    public class YoutubeThumbnailSet
    {
        public YoutubeThumbnail Default { get; set; }
    }

    public class YoutubeThumbnail
    {
        public string Url { get; set; }
    }
}