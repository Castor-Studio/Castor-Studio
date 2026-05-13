using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using CastorApplication.Models.Auth;
using CastorApplication.Models.Auth.Options;
using CastorApplication.Models.Config;
using CastorApplication.Services.Auth.Abstractions;
using CastorApplication.Services.Auth.Providers.Twitch.DTO;
using CastorApplication.Services.Config;
using TwitchLib.Api;

namespace CastorApplication.Services.Auth.Providers.Twitch
{
    public class TwitchAuthProvider : IAuthProvider
    {
        private readonly HttpClient _http;
        private readonly ProviderConfig _options;
        private readonly IDeviceAuthFlow _flow;
        private readonly TwitchSessionFactory _sessionFactory;

        public string Id => "twitch";
        public string ClientId => _options.ClientId;
        public AuthFlowType FlowType => AuthFlowType.DeviceCode;

        public TwitchAuthProvider(
            HttpClient httpClient,
            IConfigService configService,
            IDeviceAuthFlow flow,
            TwitchSessionFactory sessionFactory)
        {
            _http = httpClient;
            _options = configService.GetProviderConfig(Id);
            _flow = flow;
            _sessionFactory = sessionFactory;
        }

        public Task<DeviceCodeResult> BeginLoginAsync(CancellationToken ct = default)
        {
            return _flow.BeginAsync(ct);
        }

        public Task<AuthSession> CompleteLoginAsync(
            DeviceCodeResult deviceCode, CancellationToken ct = default)
        {
            return _flow.PollAsync(deviceCode, ct);
        }

        public async Task<AuthSession> RefreshAsync(AuthSession session, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(session.RefreshToken))
                throw new InvalidOperationException("No refresh token available.");

            var body = new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = session.RefreshToken
            };

            using var response = await _http.PostAsync(
                TwitchEndpoints.Token,
                new FormUrlEncodedContent(body),
                ct);

            response.EnsureSuccessStatusCode();

            var token =
                await response.Content.ReadFromJsonAsync<TwitchTokenResponse>(
                    cancellationToken: ct)
                ?? throw new InvalidOperationException();

            return await _sessionFactory.CreateAsync(token, ct);
        }

        public async Task RevokeAsync(AuthSession session, CancellationToken ct = default)
        {
            var body = new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId,
                ["token"] = session.AccessToken
            };

            using var response = await _http.PostAsync(
                TwitchEndpoints.Revoke,
                new FormUrlEncodedContent(body),
                ct);

            response.EnsureSuccessStatusCode();
        }
    }
}
