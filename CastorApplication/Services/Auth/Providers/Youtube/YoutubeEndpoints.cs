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

        public const string Token =
            "https://oauth2.googleapis.com/token";

        public const string Revoke =
            "https://oauth2.googleapis.com/revoke";

        public const string UserInfo =
            "https://www.googleapis.com/oauth2/v2/userinfo";
    }
}
