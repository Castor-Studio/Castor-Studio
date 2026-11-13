using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CastorApplication.Models.Auth;
using CastorApplication.Services.Auth.Providers;
using CastorApplication.Services.Auth.Storage;
using CastorApplication.Services.Diagnostics;

namespace CastorApplication.Services.Auth
{
    public class AuthService : IAuthService
    {
        private readonly ConcurrentDictionary<string, SemaphoreSlim>
            _refreshLocks = new();

        private readonly ProviderRegistry _providers;
        private readonly ITokenStore _tokenStore;
        private readonly IProviderStore _providerStore;

        public AuthService(ProviderRegistry providers, ITokenStore tokenStore, IProviderStore providerStore)
        {
            _providers = providers;
            _tokenStore = tokenStore;
            _providerStore = providerStore;
        }

        public async Task<DeviceCodeResult> BeginLoginAsync(
            string providerId, CancellationToken ct = default)
        {
            var provider = _providers.Get(providerId);

            return await provider.BeginLoginAsync(ct);
        }

        public async Task<AuthSession> CompleteLoginAsync(
            string providerId,
            DeviceCodeResult deviceCode,
            CancellationToken ct = default)
        {
            var provider = _providers.Get(providerId);

            var session = await provider.CompleteLoginAsync(deviceCode, ct);

            await _tokenStore.SaveAsync(session, ct);

            return session;
        }

        public async Task<AuthSession?> GetSessionAsync(
            string providerId,
            CancellationToken ct = default)
        {
            return await _tokenStore.GetAsync(providerId, ct);
        }

        public async Task<IReadOnlyCollection<AuthSession>> GetSessionsAsync(
            CancellationToken ct = default)
        {
            return await _tokenStore.GetAllAsync(ct);
        }

        public async Task<string?> GetAccessTokenAsync(
            string providerId,
            CancellationToken ct = default)
        {
            var refreshLock =
                _refreshLocks.GetOrAdd(
                    providerId,
                    _ => new SemaphoreSlim(1, 1));

            try
            {
                await refreshLock.WaitAsync(ct);

                var session = await _tokenStore.GetAsync(providerId, ct);

                if (session is null)
                    return null;

                if (!session.IsExpired())
                    return session.AccessToken;

                var provider = _providers.Get(providerId);

                if (string.IsNullOrWhiteSpace(session.RefreshToken))
                {
                    return null;
                }

                AuthSession refreshed;
                try
                {
                    refreshed = await provider.RefreshAsync(session, ct);
                }
                catch (InvalidGrantException exception)
                {
                    AppLog.Warning($"{providerId}: refresh token rejected, account disconnected", exception);
                    // Revoked or expired for good: retrying would fail the same way on every
                    // call. The account turns disconnected and waits for a new login.
                    await _tokenStore.DeleteAsync(providerId, ct);
                    MarkDisconnected(providerId);
                    return null;
                }

                await _tokenStore.SaveAsync(refreshed, ct);

                return refreshed.AccessToken;
            }
            finally
            {
                refreshLock.Release();
            }
        }

        public async Task LogoutAsync(
            string providerId,
            CancellationToken ct = default)
        {
            var session = await _tokenStore.GetAsync(providerId, ct);

            if (session is null)
                return;

            try
            {
                var provider = _providers.Get(providerId);

                await provider.RevokeAsync(session, ct);
            }
            catch (Exception exception)
            {
                // The local session goes anyway: the user asked to log out.
                AppLog.Warning($"{providerId}: token revocation failed", exception);
            }

            await _tokenStore.DeleteAsync(providerId, ct);
        }

        // The account stays listed, with its name, so the UI can offer to reconnect it.
        private void MarkDisconnected(string providerId)
        {
            var account = _providerStore.Get(providerId);
            if (account is not { IsConnected: true }) return;

            account.IsConnected = false;
            _providerStore.Save(account);
        }

        public string GetClientId(string providerId)
        {
            var provider = _providers.Get(providerId);
            return provider.ClientId;
        }
    }
}
