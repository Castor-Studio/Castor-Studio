using System.Collections.Generic;
using System.Net.Http;

namespace CastorApplication.Services.Auth.Providers.Twitch.DTO
{
    public class TwitchRefreshRequest
    {
        public string ClientId { get; set; }
        public string GrantType { get; set; }
        public string RefreshToken { get; set; }

        public FormUrlEncodedContent ToFormContent()
        {
            var values = new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["grant_type"] = GrantType,
                ["refresh_token"] = RefreshToken
            };

            return new FormUrlEncodedContent(values);
        }
    }
}