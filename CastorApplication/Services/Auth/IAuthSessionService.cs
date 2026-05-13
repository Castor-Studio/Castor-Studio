using CastorApplication.Models.Auth;
using CastorApplication.Services.Auth.Abstractions;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CastorApplication.Services.Auth
{
    public interface IAuthSessionService
    {
        IAuthProvider GetProvider(
            string providerId);

        Task<AuthSession?> GetSessionAsync(
            string providerId,
            CancellationToken ct = default);

        Task<IReadOnlyCollection<AuthSession>>
            GetSessionsAsync(
                CancellationToken ct = default);

        Task<string?> GetAccessTokenAsync(
            string providerId,
            CancellationToken ct = default);

        Task SaveSessionAsync(
            AuthSession session,
            CancellationToken ct = default);

        Task LogoutAsync(
            string providerId,
            CancellationToken ct = default);
    }
}