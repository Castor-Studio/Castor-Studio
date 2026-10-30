using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using Castor.AI;

namespace CastorApplication.Services.Ai;

internal sealed class AiWorkerInstallException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>
/// Keeps the Castor.AI worker in a per-user folder, next to the models. It comes from the GitHub
/// release of the Castor.AI package in use: the SDK and the worker share the gRPC contract and the
/// shared memory layout, they must always match.
/// </summary>
internal sealed class AiWorkerInstaller
{
    internal const string WorkerExeName = "Castor.AI.Worker.exe";
    internal const string VersionMarkerName = "castor-ai-worker.version";

    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly string _root;
    private readonly string _version;

    public AiWorkerInstaller(HttpClient http, string? root = null, string? version = null)
    {
        _http = http;
        _root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Castor", "worker");
        _version = version ?? PackageVersion();
    }

    /// <summary>Returns the worker executable, downloading it when missing or of another version.</summary>
    /// <exception cref="AiWorkerInstallException">The download failed or the zip is corrupted.</exception>
    public async Task<string> EnsureInstalledAsync(Action<string> progress, CancellationToken cancellationToken)
    {
        var target = Path.Combine(_root, _version);
        if (!IsInstalled(target))
        {
            try
            {
                await InstallAsync(target, progress, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException
                or UnauthorizedAccessException or InvalidDataException
                || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                throw new AiWorkerInstallException(
                    $"Échec du téléchargement du worker IA {_version} : {exception.Message}", exception);
            }

            RemoveOtherVersions(target);
        }

        return Path.Combine(target, WorkerExeName);
    }

    private async Task InstallAsync(string target, Action<string> progress, CancellationToken cancellationToken)
    {
        var asset = $"Castor.AI.Worker-{_version}-win-x64.zip";
        var baseUrl = $"https://github.com/Castor-Studio/Castor-AI-Worker/releases/download/v{_version}";

        Directory.CreateDirectory(_root);
        // Dot-prefixed so that RemoveOtherVersions leaves the work of another instance alone.
        var staging = Path.Combine(_root, $".{_version}-{Guid.NewGuid():N}");
        var zip = staging + ".zip";
        try
        {
            progress("téléchargement du worker IA");
            var published = await _http.GetStringAsync($"{baseUrl}/{asset}.sha256", cancellationToken);
            var expected = published.Trim().Split(' ')[0].ToLowerInvariant();

            var actual = await DownloadAsync($"{baseUrl}/{asset}", zip, progress, cancellationToken);
            if (actual != expected)
                throw new AiWorkerInstallException($"Worker IA corrompu ({asset}) : SHA-256 {actual}, attendu {expected}.");

            progress("installation du worker IA");
            await Task.Run(() => ZipFile.ExtractToDirectory(zip, staging), cancellationToken);
            if (!File.Exists(Path.Combine(staging, WorkerExeName)))
                throw new AiWorkerInstallException($"{asset} ne contient pas {WorkerExeName}.");
            await File.WriteAllTextAsync(Path.Combine(staging, VersionMarkerName), _version, cancellationToken);

            // A moved folder is complete: an interrupted install never looks installed.
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            try { Directory.Move(staging, target); }
            catch (IOException) when (IsInstalled(target)) { } // Another instance got there first.
        }
        finally
        {
            TryDelete(() => File.Delete(zip));
            TryDelete(() => { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); });
        }
    }

    /// <returns>The lowercase SHA-256 of the file.</returns>
    private async Task<string> DownloadAsync(string url, string path, Action<string> progress,
        CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var length = response.Content.Headers.ContentLength;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var file = File.Create(path);

        // HttpClient.Timeout stops at the headers: without this, a dead connection hangs for good.
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var buffer = new byte[81920];
        long received = 0;
        var lastPercent = -1;
        while (true)
        {
            stall.CancelAfter(StallTimeout);
            var read = await source.ReadAsync(buffer, stall.Token);
            if (read == 0) break;

            hash.AppendData(buffer, 0, read);
            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;

            if (length is > 0 && (int)(received * 100 / length.Value) is var percent && percent != lastPercent)
            {
                lastPercent = percent;
                progress($"téléchargement du worker IA {percent} %");
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private bool IsInstalled(string directory)
    {
        var marker = Path.Combine(directory, VersionMarkerName);
        return File.Exists(Path.Combine(directory, WorkerExeName))
            && File.Exists(marker)
            && File.ReadAllText(marker).Trim() == _version;
    }

    // Best effort: a previous version may still run in another instance of the app.
    private void RemoveOtherVersions(string target)
    {
        foreach (var directory in Directory.GetDirectories(_root))
        {
            if (directory != target && !Path.GetFileName(directory).StartsWith('.'))
                TryDelete(() => Directory.Delete(directory, recursive: true));
        }
    }

    private static void TryDelete(Action delete)
    {
        try { delete(); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string PackageVersion() =>
        typeof(AiClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+')[0];
}
