using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CastorApplication.Models.Auth;

namespace CastorApplication.Services.Auth
{
    public interface IAuthService
    {
        Task<DeviceCodeResult> BeginLoginAsync(
            string providerId,
            CancellationToken cancellationToken = default);

        Task<AuthSession> CompleteLoginAsync(
            string providerId,
            DeviceCodeResult deviceCode,
            CancellationToken cancellationToken = default);

        Task<AuthSession?> GetSessionAsync(
            string providerId,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyCollection<AuthSession>> GetSessionsAsync(
            CancellationToken cancellationToken = default);

        Task<string?> GetAccessTokenAsync(
            string providerId,
            CancellationToken cancellationToken = default);

        Task LogoutAsync(
            string providerId,
            CancellationToken cancellationToken = default);
    }
}
