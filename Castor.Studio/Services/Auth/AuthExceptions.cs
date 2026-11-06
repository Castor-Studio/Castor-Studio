namespace CastorApplication.Services.Auth;

// The user refused the consent screen: retrying silently would only ask again.
public sealed class AuthorizationDeniedException(string message) : Exception(message);

// The refresh token was revoked or expired: only a new login brings the session back.
public sealed class InvalidGrantException(string providerId)
    : Exception($"La session {providerId} a expiré ou a été révoquée. Reconnectez le compte.")
{
    public string ProviderId { get; } = providerId;
}

// The provider has no OAuth client in config.json or in the environment.
public sealed class AuthNotConfiguredException(string message) : Exception(message);
