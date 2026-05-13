using CastorApplication.Models.Auth;
using CastorApplication.Services.Auth.Abstractions;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CastorApplication.Services.Auth
{
    public class AuthService : IAuthService
    {
        private readonly IAuthSessionService _sessionService;

        public AuthService(IAuthSessionService sessionService)
        {
            _sessionService = sessionService;
        }

        public async Task<AuthSession> LoginAsync(
            string providerId,
            CancellationToken ct = default)
        {
            var provider =
                _sessionService
                    .GetProvider(providerId);

            AuthSession session;

            switch (provider.Flow)
            {
                case IDeviceAuthFlow deviceFlow:
                {
                    var login = await deviceFlow.BeginAsync(ct);

                    BrowserLauncher.Open(login.VerificationUri);

                    session = await deviceFlow.PollAsync(login, ct);

                    break;
                }

                case IPkceAuthFlow pkceFlow:
                {
                    var login = await pkceFlow.BeginAsync(ct);

                    session = await pkceFlow.CompleteAsync(login, ct);

                    break;
                }

                default:
                    throw new NotSupportedException(
                        $"Unsupported auth flow for provider '{providerId}'.");
            }

            await _sessionService.SaveSessionAsync(session, ct);

            return session;
        }
    }
}