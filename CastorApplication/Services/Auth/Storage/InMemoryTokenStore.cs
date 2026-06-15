using CastorApplication.Models.Auth;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CastorApplication.Services.Auth.Storage
{
    public sealed class InMemoryTokenStore : ITokenStore
    {
        private readonly Dictionary<string, AuthSession>
            _sessions = [];

        public Task SaveAsync(
            AuthSession session,
            CancellationToken ct = default)
        {
            _sessions[session.ProviderId] = session;

            return Task.CompletedTask;
        }

        public Task<AuthSession?> GetAsync(
            string providerId,
            CancellationToken ct = default)
        {
            _sessions.TryGetValue(
                providerId,
                out var session);

            return Task.FromResult(session);
        }

        public Task DeleteAsync(
            string providerId,
            CancellationToken ct = default)
        {
            _sessions.Remove(providerId);

            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<AuthSession>>GetAllAsync(
            CancellationToken ct = default)
        {
            return Task.FromResult(
                (IReadOnlyCollection<AuthSession>)
                _sessions.Values.ToList());
        }
    }
}
