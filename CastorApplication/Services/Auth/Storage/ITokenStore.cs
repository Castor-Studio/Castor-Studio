using CastorApplication.Models.Auth;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CastorApplication.Services.Auth.Storage
{
    public interface ITokenStore
    {
        Task SaveAsync(
            AuthSession session,
            CancellationToken ct = default);

        Task<AuthSession?> GetAsync(
            string providerId,
            CancellationToken ct = default);

        Task DeleteAsync(
            string providerId,
            CancellationToken ct = default);

        Task<IReadOnlyCollection<AuthSession>> GetAllAsync(
            CancellationToken ct = default);
    }
}
