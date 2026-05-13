using CastorApplication.Models.Auth;
using CastorApplication.Models.Config;
using CastorApplication.Services.Auth.Abstractions;
using CastorApplication.Services.Auth.Providers.Twitch.DTO;
using CastorApplication.Services.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CastorApplication.Services.Auth.Providers.Twitch
{
    public class TwitchDeviceAuthFlow : IDeviceAuthFlow
    {
        private readonly HttpClient _http;
        private readonly ProviderConfig _options;
        private readonly TwitchSessionFactory _sessionFactory;

        public TwitchDeviceAuthFlow(
            HttpClient http,
            IConfigService config,
            TwitchSessionFactory sessionFactory)
        {
            _http = http;
            _options = config.GetProviderConfig("twitch");
            _sessionFactory = sessionFactory;
        }

        public async Task<DeviceCodeResult> BeginAsync(CancellationToken ct = default)
        {
            var body = new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId,
                ["scopes"] = string.Join(' ', _options.Scopes)
            };

            using var response = await _http.PostAsync(
                TwitchEndpoints.DeviceCode,
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

        public async Task<AuthSession> PollAsync(
            DeviceCodeResult deviceCode, CancellationToken ct = default)
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
                    TwitchEndpoints.Token,
                    new FormUrlEncodedContent(body),
                    ct);

                if (response.IsSuccessStatusCode)
                {
                    var token =
                        await response.Content.ReadFromJsonAsync<TwitchTokenResponse>(
                            cancellationToken: ct)
                        ?? throw new InvalidOperationException();

                    return await _sessionFactory.CreateAsync(token, ct);
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
    }
}
