using CastorApplication.Models.Auth;
using CastorApplication.Models.Auth.Options;
using CastorApplication.Models.Config;
using CastorApplication.Services.Auth.Providers.Twitch.DTO;
using CastorApplication.Services.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using TwitchLib.Api;

namespace CastorApplication.Services.Auth.Providers.Twitch
{
    public class TwitchAuthProvider : IAuthProvider
    {
        public string Id => "twitch";

        private readonly HttpClient _http;
        private readonly ProviderConfig _options;

        private const string _deviceCodeEndpoint = "https://id.twitch.tv/oauth2/device";
        private const string _tokenEndpoint = "https://id.twitch.tv/oauth2/token";
        private const string _revokeEndpoint = "https://id.twitch.tv/oauth2/revoke";

        public string ClientId => _options.ClientId;

        public TwitchAuthProvider(HttpClient httpClient, IConfigService configService)
        {
            _http = httpClient;
            _options = configService.GetProviderConfig(Id);
        }

        public async Task<DeviceCodeResult> BeginLoginAsync(CancellationToken ct = default)
        {
            var body = new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId,
                ["scopes"] = string.Join(' ', _options.Scopes)
            };

            using var response = await _http.PostAsync(
                _deviceCodeEndpoint,
                new FormUrlEncodedContent(body),
                ct);

            response.EnsureSuccessStatusCode();

            var dto =
                await response.Content.ReadFromJsonAsync<TwitchDeviceCodeResponse>(
                    cancellationToken: ct)
                ?? throw new InvalidOperationException(
                    "Failed to parse device code response.");

            return new DeviceCodeResult
            {
                DeviceCode = dto.DeviceCode,
                UserCode = dto.UserCode,
                VerificationUri = dto.VerificationUri,
                ExpiresIn = TimeSpan.FromSeconds(dto.ExpiresIn),
                Interval = TimeSpan.FromSeconds(dto.Interval)
            };
        }

        public async Task<AuthSession> CompleteLoginAsync(DeviceCodeResult deviceCode, CancellationToken ct = default)
        {
            var interval = deviceCode.Interval;

            while (!deviceCode.IsExpired)
            {
                ct.ThrowIfCancellationRequested();

                await Task.Delay(interval, ct);

                var body = new Dictionary<string, string>
                {
                    ["client_id"] = _options.ClientId,
                    ["device_code"] = deviceCode.DeviceCode,
                    ["grant_type"] =
                        "urn:ietf:params:oauth:grant-type:device_code"
                };

                using var response = await _http.PostAsync(
                    _tokenEndpoint,
                    new FormUrlEncodedContent(body),
                    ct);

                if (response.IsSuccessStatusCode)
                {
                    var token =
                        await response.Content.ReadFromJsonAsync<TwitchTokenResponse>(
                            cancellationToken: ct)
                        ?? throw new InvalidOperationException();

                    return await CreateSessionAsync(token, ct);
                }

                var error =
                    await response.Content.ReadAsStringAsync(ct);

                if (error.Contains("authorization_pending"))
                    continue;

                if (error.Contains("slow_down"))
                {
                    interval += TimeSpan.FromSeconds(5);
                    continue;
                }

                throw new InvalidOperationException($"Twitch auth failed: {error}");
            }

            throw new TimeoutException("Device code expired.");
        }

        public async Task<UserProfile> GetProfileAsync(AuthSession session, CancellationToken ct = default)
        {
            var api = new TwitchAPI();
            api.Settings.ClientId = _options.ClientId;
            api.Settings.AccessToken = session.AccessToken;

            var users = await api.Helix.Users.GetUsersAsync();

            var user = users.Users.FirstOrDefault()
                ?? throw new InvalidOperationException("Failed to get Twitch user profile.");

            return new UserProfile
            {
                Id = user.Id,
                DisplayName = user.DisplayName,
                AvatarUrl = user.ProfileImageUrl
            };
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
                _tokenEndpoint,
                new FormUrlEncodedContent(body),
                ct);

            response.EnsureSuccessStatusCode();

            var token =
                await response.Content.ReadFromJsonAsync<TwitchTokenResponse>(
                    cancellationToken: ct)
                ?? throw new InvalidOperationException();

            return await CreateSessionAsync(token, ct);
        }

        public async Task RevokeAsync(AuthSession session, CancellationToken ct = default)
        {
            var body = new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId,
                ["token"] = session.AccessToken
            };

            using var response = await _http.PostAsync(
                _revokeEndpoint,
                new FormUrlEncodedContent(body),
                ct);

            response.EnsureSuccessStatusCode();
        }

        private async Task<AuthSession> CreateSessionAsync(
            TwitchTokenResponse token,
            CancellationToken ct)
        {
            var session = new AuthSession
            {
                ProviderId = Id,
                AccessToken = token.AccessToken,
                RefreshToken = token.RefreshToken,
                ExpiresAt = DateTimeOffset.UtcNow
                    .AddSeconds(token.ExpiresIn),
                Scopes = token.Scope
            };

            session.Profile =
                await GetProfileAsync(session, ct);

            return session;
        }
    }
}
