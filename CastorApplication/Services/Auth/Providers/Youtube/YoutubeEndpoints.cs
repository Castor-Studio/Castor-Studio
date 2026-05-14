using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CastorApplication.Services.Auth.Providers.Youtube
{
    public static class YoutubeEndpoints
    {
        public const string Authorization =
            "https://accounts.google.com/o/oauth2/v2/auth";

        public const string RedirectUri =
            "http://127.0.0.1:45678/auth/youtube/callback";

        public const string Token =
            "https://oauth2.googleapis.com/token";

        public const string Revoke =
            "https://oauth2.googleapis.com/revoke";

        public const string UserInfo =
            "https://youtube.googleapis.com/youtube/v3/channels?part=snippet&mine=true";

        public const string StreamKey =
            "https://www.googleapis.com/youtube/v3/liveStreams?part=snippet,cdn&mine=true";
    }
}
