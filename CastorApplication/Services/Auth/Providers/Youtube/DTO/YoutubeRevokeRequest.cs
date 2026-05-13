using System.Collections.Generic;
using System.Net.Http;

namespace CastorApplication.Services.Auth.Providers.Youtube.DTO
{
    internal class YoutubeRevokeRequest
    {
        public string Token { get; set; }

        public FormUrlEncodedContent ToFormContent()
        {
            var values = new Dictionary<string, string>
            {
                ["token"] = Token
            };

            return new FormUrlEncodedContent(values);
        }
    }
}