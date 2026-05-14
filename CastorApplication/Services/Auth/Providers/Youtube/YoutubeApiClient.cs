using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CastorApplication.Services.Auth.Providers.Youtube.DTO;

namespace CastorApplication.Services.Auth.Providers.Youtube
{
    public class YoutubeApiClient
    {
        private readonly HttpClient _http;

        public YoutubeApiClient(HttpClient httpClient)
        {
            _http = httpClient;
        }

        public async Task<string?> GetStreamKeyAsync(string accessToken, CancellationToken ct = default)
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _http.GetAsync(YoutubeEndpoints.StreamKey, ct);

            response.EnsureSuccessStatusCode();

            var result = await response.Content
                    .ReadFromJsonAsync<YoutubeLiveStreamsResponse>(cancellationToken: ct)
                ?? throw new InvalidOperationException("Invalid YouTube response");

            return result.Items?
                .FirstOrDefault()?
                .Cdn?
                .IngestionInfo?
                .StreamName;
        }
    }
}
