using CastorApplication.Models.Auth;
using CastorApplication.Models.Config;
using CastorApplication.Services.Auth.Providers.Twitch.DTO;
using CastorApplication.Services.Config;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TwitchLib.Api;

namespace CastorApplication.Services.Auth.Providers.Twitch
{
    public class TwitchSessionFactory
    {
        private readonly ProviderConfig _providerConfig;
        private readonly TwitchAPI _api;

        public TwitchSessionFactory(IConfigService configService, TwitchAPI api)
        {
            _providerConfig = configService.GetProviderConfig("twitch");
            _api = api;
            _api.Settings.ClientId = _providerConfig.ClientId;
        }

        public async Task<AuthSession> CreateAsync(
            TwitchTokenResponse token,
            CancellationToken ct = default)
        {
            var session = new AuthSession
            {
                ProviderId = "twitch",
                AccessToken = token.AccessToken,
                RefreshToken = token.RefreshToken,
                ExpiresAt = DateTimeOffset.UtcNow
                    .AddSeconds(token.ExpiresIn),
                Scopes = token.Scope
            };

            session.Profile = await GetProfileAsync(session);

            return session;
        }

        private async Task<UserProfile> GetProfileAsync(AuthSession session)
        {
            _api.Settings.AccessToken = session.AccessToken;

            var users = await _api.Helix.Users.GetUsersAsync();

            var user = users.Users.FirstOrDefault()
                ?? throw new InvalidOperationException("Failed to get Twitch user profile.");

            return new UserProfile
            {
                Id = user.Id,
                DisplayName = user.DisplayName,
                AvatarUrl = user.ProfileImageUrl
            };
        }
    }
}