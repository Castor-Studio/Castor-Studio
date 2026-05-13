using System.Collections.Generic;
using System.Net.Http;

namespace CastorApplication.Services.Auth.Providers.Youtube
{
    internal class YoutubeRefreshRequest
    {
        public string ClientId { get; set; }
        public string RefreshToken { get; set; }
        public string GrantType { get; set; }

        public FormUrlEncodedContent ToFormContent()
        {
            var values = new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["refresh_token"] = RefreshToken,
                ["grant_type"] = GrantType
            };

            return new FormUrlEncodedContent(values);
        }
    }
}