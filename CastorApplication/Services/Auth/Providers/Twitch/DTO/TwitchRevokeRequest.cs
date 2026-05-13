using System.Collections.Generic;
using System.Net.Http;

namespace CastorApplication.Services.Auth.Providers.Twitch.DTO
{
    public class TwitchRevokeRequest
    {
        public string ClientId { get; set; }
        public string Token { get; set; }

        public FormUrlEncodedContent ToFormContent()
        {
            var values = new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["token"] = Token
            };
            
            return new FormUrlEncodedContent(values);
        }
    }
}