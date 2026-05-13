using CastorApplication.Models.Auth;
using System.Threading;
using System.Threading.Tasks;

namespace CastorApplication.Services.Auth.Abstractions
{
    public interface IAuthProvider
    {
        string Id { get; }

        string ClientId { get; }

        IAuthFlow Flow { get; }

        Task<AuthSession> RefreshAsync(
            AuthSession session,
            CancellationToken ct = default);

        Task RevokeAsync(
            AuthSession session,
            CancellationToken ct = default);
    }
}
