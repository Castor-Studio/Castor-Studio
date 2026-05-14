using CastorApplication.Models.Auth;
using CastorApplication.Services.Auth.Providers.Youtube.DTO;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CastorApplication.Services.Auth.Providers.Youtube
{
    public class YoutubeSessionFactory
    {
        private readonly HttpClient _http;

        public YoutubeSessionFactory(HttpClient http)
        {
            _http = http;
        }

        public async Task<AuthSession> CreateAsync(
            YoutubeTokenResponse token,
            CancellationToken ct = default)
        {
            var session = new AuthSession
            {
                ProviderId = "youtube",
                AccessToken = token.AccessToken,
                RefreshToken = token.RefreshToken,
                ExpiresAt = DateTimeOffset.UtcNow
                    .AddSeconds(token.ExpiresIn),
                Scopes = token.Scope
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            };

            session.Profile =
                await GetProfileAsync(session, ct);

            return session;
        }

        private async Task<UserProfile> GetProfileAsync(
            AuthSession session,
            CancellationToken ct = default)
        {
            using var request =
                new HttpRequestMessage(HttpMethod.Get, YoutubeEndpoints.UserInfo);

            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", session.AccessToken);

            using var response =
                await _http.SendAsync(request, ct);

            var content =
                await response.Content.ReadAsStringAsync(ct);

            response.EnsureSuccessStatusCode();

            var result = await response.Content
                    .ReadFromJsonAsync<YoutubeChannelsResponse>(cancellationToken: ct)
                ?? throw new InvalidOperationException("Invalid YouTube response");

            var channel = result.Items.FirstOrDefault()
                ?? throw new InvalidOperationException("No channel found");

            return new UserProfile
            {
                Id = channel.Id,
                DisplayName = channel.Snippet.Title,
                AvatarUrl = channel.Snippet.Thumbnails.Default.Url
            };
        }
    }
}