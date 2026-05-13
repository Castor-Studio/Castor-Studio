using CastorApplication.Models.Auth;
using CastorApplication.Models.Config;
using CastorApplication.Services.Auth.Abstractions;
using CastorApplication.Services.Auth.Providers.Youtube.DTO;
using CastorApplication.Services.Config;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CastorApplication.Services.Auth.Providers.Youtube
{
    public class YoutubeAuthProvider : IAuthProvider
    {
        private readonly HttpClient _http;
        private readonly ProviderConfig _config;
        private readonly YoutubeSessionFactory _sessionFactory;

        public string Id => "youtube";

        public string ClientId => _config.ClientId;

        public IAuthFlow Flow { get; }

        public YoutubeAuthProvider(
            HttpClient httpClient,
            IConfigService config,
            YoutubeAuthFlow flow,
            YoutubeSessionFactory sessionFactory)
        {
            _http = httpClient;

            _config =
                config.GetProviderConfig(Id);

            Flow = flow;

            _sessionFactory = sessionFactory;
        }

        public async Task<AuthSession> RefreshAsync(
            AuthSession session,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(
                session.RefreshToken))
            {
                throw new InvalidOperationException(
                    "No refresh token available.");
            }

            var request =
                new YoutubeRefreshRequest
                {
                    ClientId = _config.ClientId,
                    RefreshToken =
                        session.RefreshToken,
                    GrantType = "refresh_token"
                };

            using var response =
                await _http.PostAsync(
                    YoutubeEndpoints.Token,
                    request.ToFormContent(),
                    ct);

            response.EnsureSuccessStatusCode();

            var token =
                await response.Content
                    .ReadFromJsonAsync<
                        YoutubeTokenResponse>(
                        cancellationToken: ct)
                ?? throw new InvalidOperationException(
                    "Failed to parse token response.");

            return await _sessionFactory
                .CreateAsync(token, ct);
        }

        public async Task RevokeAsync(
            AuthSession session,
            CancellationToken ct = default)
        {
            var request =
                new YoutubeRevokeRequest
                {
                    Token = session.AccessToken
                };

            using var response =
                await _http.PostAsync(
                    YoutubeEndpoints.Revoke,
                    request.ToFormContent(),
                    ct);

            response.EnsureSuccessStatusCode();
        }
    }
}