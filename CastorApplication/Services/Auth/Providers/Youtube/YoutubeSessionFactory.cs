using CastorApplication.Models.Auth;
using CastorApplication.Services.Auth.Providers.Youtube.DTO;
using System;
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
                new HttpRequestMessage(
                    HttpMethod.Get,
                    YoutubeEndpoints.UserInfo);

            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    session.AccessToken);

            using var response =
                await _http.SendAsync(request, ct);

            response.EnsureSuccessStatusCode();

            var profile =
                await response.Content
                    .ReadFromJsonAsync<YoutubeProfileResponse>(
                        cancellationToken: ct)
                ?? throw new InvalidOperationException(
                    "Failed to parse YouTube profile.");

            return new UserProfile
            {
                Id = profile.Id,
                DisplayName = profile.Name,
                AvatarUrl = profile.Picture
            };
        }
    }
}