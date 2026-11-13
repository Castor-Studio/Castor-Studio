using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CastorApplication.Models.Auth;
using CastorApplication.Services.Diagnostics;

namespace CastorApplication.Services.Auth.Storage;

// Keeps provider sessions across restarts: YouTube needs a valid access token at every live,
// which only a stored refresh token can give without a new login. The file is encrypted with
// DPAPI for the current Windows user, so it is unreadable as text and on another account.
[SupportedOSPlatform("windows")]
public sealed class EncryptedFileTokenStore : ITokenStore
{
    // Binds the file to Castor: another app of the same user cannot decrypt it by chance.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Castor Studio auth sessions v1");

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    // Read once, then kept in step with every write.
    private Dictionary<string, AuthSession>? _sessions;

    public EncryptedFileTokenStore()
        : this(DefaultPath)
    {
    }

    public EncryptedFileTokenStore(string filePath)
    {
        _filePath = filePath;
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "castor-studio",
        "tokens.dat");

    public async Task SaveAsync(AuthSession session, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var sessions = LoadCore();
            sessions[session.ProviderId] = session;
            await WriteCoreAsync(sessions, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AuthSession?> GetAsync(string providerId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return LoadCore().GetValueOrDefault(providerId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(string providerId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var sessions = LoadCore();
            if (sessions.Remove(providerId))
                await WriteCoreAsync(sessions, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyCollection<AuthSession>> GetAllAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return LoadCore().Values.ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    private Dictionary<string, AuthSession> LoadCore()
    {
        if (_sessions != null) return _sessions;

        _sessions = new Dictionary<string, AuthSession>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(_filePath)) return _sessions;

            var json = ProtectedData.Unprotect(
                File.ReadAllBytes(_filePath),
                Entropy,
                DataProtectionScope.CurrentUser);
            var stored = JsonSerializer.Deserialize<List<AuthSession>>(json) ?? [];
            foreach (var session in stored.Where(session => !string.IsNullOrWhiteSpace(session.ProviderId)))
                _sessions[session.ProviderId] = session;
        }
        // Corrupted, or encrypted by another Windows account: the user reconnects, nothing more.
        // The next save replaces the file.
        catch (Exception exception) when (exception is CryptographicException or JsonException or IOException)
        {
            AppLog.Warning($"Stored accounts unreadable, every account is disconnected: {_filePath}", exception);
            _sessions.Clear();
        }

        return _sessions;
    }

    // Written aside then moved over the old file: a crash mid-write never leaves half a file.
    private async Task WriteCoreAsync(Dictionary<string, AuthSession> sessions, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var json = JsonSerializer.SerializeToUtf8Bytes(sessions.Values.ToList());
        var encrypted = ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser);

        var temporaryPath = _filePath + ".tmp";
        await File.WriteAllBytesAsync(temporaryPath, encrypted, ct);
        File.Move(temporaryPath, _filePath, overwrite: true);
    }
}
