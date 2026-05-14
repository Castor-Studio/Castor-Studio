using CastorApplication.Models.Auth;
using CastorApplication.Models.Auth.PKCE;
using CastorApplication.Models.Config;
using CastorApplication.Services.Auth.Abstractions;
using CastorApplication.Services.Auth.Common.PKCE;
using CastorApplication.Services.Auth.Providers.Twitch;
using CastorApplication.Services.Auth.Providers.Twitch.DTO;
using CastorApplication.Services.Auth.Providers.Youtube.DTO;
using CastorApplication.Services.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CastorApplication.Services.Auth.Providers.Youtube
{
    public class YoutubeAuthFlow : IPkceAuthFlow
    {
        private readonly HttpClient _http;
        private readonly ProviderConfig _options;

        private readonly PkceGenerator _pkce;
        private readonly ILocalAuthServer _localServer;

        private readonly YoutubeSessionFactory _sessionFactory;

        public YoutubeAuthFlow(
            HttpClient http,
            IConfigService configService,
            PkceGenerator pkce,
            ILocalAuthServer localServer,
            YoutubeSessionFactory sessionFactory)
        {
            _http = http;
            _options = configService.GetProviderConfig("youtube");
            _pkce = pkce;
            _localServer = localServer;
            _sessionFactory = sessionFactory;
        }

        public async Task<BrowserLoginResult> BeginAsync(CancellationToken ct = default)
        {
            var pkce = _pkce.Generate();

            var state =
                Guid.NewGuid().ToString("N");

            var scopes =
                string.Join(' ', _options.Scopes);

            var query =
                new Dictionary<string, string>
                {
                    ["client_id"] = _options.ClientId,
                    ["redirect_uri"] = YoutubeEndpoints.RedirectUri,
                    ["response_type"] = "code",
                    ["scope"] = scopes,
                    ["state"] = state,
                    ["code_challenge"] = pkce.Challenge,
                    ["code_challenge_method"] = "S256",
                    ["access_type"] = "offline",
                    ["prompt"] = "consent"
                };

            var queryString =
                string.Join("&",
                    query.Select(x =>
                        $"{Uri.EscapeDataString(x.Key)}=" +
                        $"{Uri.EscapeDataString(x.Value)}"));

            var authUrl =
                $"{YoutubeEndpoints.Authorization}?{queryString}";

            BrowserLauncher.Open(authUrl);

            return await Task.FromResult(
                new BrowserLoginResult
                {
                    State = state,
                    CodeVerifier = pkce.Verifier,
                    AuthorizationUri = new Uri(authUrl)
                });
        }

        public async Task<AuthSession> CompleteAsync(BrowserLoginResult login, CancellationToken ct = default)
        {
            var callback = await _localServer.WaitForCallbackAsync(ct);

            if (callback.State != login.State)
            {
                throw new InvalidOperationException("Invalid OAuth state.");
            }

            var body =
                new Dictionary<string, string>
                {
                    ["client_id"] = _options.ClientId,
                    ["client_secret"] = _options.ClientSecret!,
                    ["code"] = callback.Code,
                    ["code_verifier"] = login.CodeVerifier,
                    ["grant_type"] = "authorization_code",
                    ["redirect_uri"] = YoutubeEndpoints.RedirectUri
                };

            using var response =
                await _http.PostAsync(
                    YoutubeEndpoints.Token,
                    new FormUrlEncodedContent(body),
                    ct);

            response.EnsureSuccessStatusCode();

            var token =
                await response.Content
                    .ReadFromJsonAsync<
                        YoutubeTokenResponse>(
                        cancellationToken: ct)
                ?? throw new InvalidOperationException(
                    "Failed to parse YouTube token response.");

            var session = await _sessionFactory.CreateAsync(token, ct);

            return session;
        }
    }
}
