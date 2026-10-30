using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Castor.AI.Providers;
using CastorApplication.Services.Ai;

namespace Castor.Studio.Tests;

public sealed class AiWorkerInstallerTests : IDisposable
{
    private const string Version = "1.2.3";
    private const string ZipUrl =
        "https://github.com/Castor-Studio/Castor-AI-Worker/releases/download/v1.2.3/Castor.AI.Worker-1.2.3-win-x64.zip";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "castor-worker-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeReleaseHandler _release = new();
    private readonly AiWorkerInstaller _installer;
    private readonly List<string> _progress = [];

    public AiWorkerInstallerTests()
    {
        _installer = new AiWorkerInstaller(new HttpClient(_release), _root, Version);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task A_missing_worker_is_downloaded_with_its_progress()
    {
        _release.Publish(WorkerZip());

        var path = await EnsureAsync();

        Assert.Equal(Path.Combine(_root, Version, AiWorkerInstaller.WorkerExeName), path);
        Assert.True(File.Exists(path));
        Assert.Equal(Version, File.ReadAllText(Path.Combine(_root, Version, AiWorkerInstaller.VersionMarkerName)));
        Assert.Contains("téléchargement du worker IA 100 %", _progress);
        Assert.Equal([Path.Combine(_root, Version)], Directory.GetFileSystemEntries(_root));
    }

    [Fact]
    public async Task An_installed_worker_is_not_downloaded_again()
    {
        _release.Publish(WorkerZip());
        await EnsureAsync();
        _release.Requests.Clear();

        await EnsureAsync();

        Assert.Empty(_release.Requests);
    }

    [Fact]
    public async Task An_install_without_marker_is_replaced()
    {
        var incomplete = Directory.CreateDirectory(Path.Combine(_root, Version));
        File.WriteAllText(Path.Combine(incomplete.FullName, AiWorkerInstaller.WorkerExeName), "old");
        _release.Publish(WorkerZip());

        var path = await EnsureAsync();

        Assert.Equal("worker", File.ReadAllText(path));
    }

    [Fact]
    public async Task A_previous_version_is_removed()
    {
        var previous = Directory.CreateDirectory(Path.Combine(_root, "1.0.0"));
        _release.Publish(WorkerZip());

        await EnsureAsync();

        Assert.False(previous.Exists);
    }

    [Fact]
    public async Task A_bad_checksum_installs_nothing()
    {
        _release.Publish(WorkerZip(), checksum: new string('0', 64));

        var exception = await Assert.ThrowsAsync<AiWorkerInstallException>(EnsureAsync);

        Assert.Contains("SHA-256", exception.Message);
        Assert.Empty(Directory.GetFileSystemEntries(_root));
    }

    [Fact]
    public async Task A_zip_without_the_worker_installs_nothing()
    {
        _release.Publish(WorkerZip(entry: "other.exe"));

        await Assert.ThrowsAsync<AiWorkerInstallException>(EnsureAsync);

        Assert.Empty(Directory.GetFileSystemEntries(_root));
    }

    [Fact]
    public async Task Being_offline_gives_a_clear_error()
    {
        _release.Failure = new HttpRequestException("offline");

        var exception = await Assert.ThrowsAsync<AiWorkerInstallException>(EnsureAsync);

        Assert.Contains("offline", exception.Message);
    }

    [Fact]
    public async Task A_cancelled_install_stays_a_cancellation()
    {
        _release.Publish(WorkerZip());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _installer.EnsureInstalledAsync(_progress.Add, cancelled.Token));
    }

    [Fact]
    public async Task The_provider_reports_the_install_then_runs_the_installed_worker()
    {
        _release.Publish(WorkerZip());
        var worker = new FakeAiProvider();
        string? startedPath = null;
        var provider = new InstallingWorkerProvider(_installer, path =>
        {
            startedPath = path;
            return worker;
        });
        var states = new List<string>();
        provider.StateChanged += (_, detail) => states.Add(detail);

        await provider.StartAsync(Request);

        Assert.Equal(Path.Combine(_root, Version, AiWorkerInstaller.WorkerExeName), startedPath);
        Assert.Contains("téléchargement du worker IA 100 %", states);
        Assert.Contains("running", states);
        Assert.Single(worker.Requests);
    }

    [Fact]
    public async Task The_provider_reports_a_failed_install()
    {
        _release.Failure = new HttpRequestException("offline");
        var provider = new InstallingWorkerProvider(_installer, _ => new FakeAiProvider());
        var states = new List<AiConnectionState>();
        provider.StateChanged += (state, _) => states.Add(state);

        await Assert.ThrowsAsync<AiWorkerInstallException>(() => provider.StartAsync(Request));

        Assert.Contains(AiConnectionState.Faulted, states);
    }

    private static AiProviderRequest Request => new("podcast", [], 1, new Dictionary<string, string>(), () => 0);

    private Task<string> EnsureAsync() => _installer.EnsureInstalledAsync(_progress.Add, CancellationToken.None);

    private static byte[] WorkerZip(string entry = AiWorkerInstaller.WorkerExeName)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry(entry).Open(), new UTF8Encoding(false));
            writer.Write("worker");
        }
        return stream.ToArray();
    }

    private sealed class FakeReleaseHandler : HttpMessageHandler
    {
        private byte[] _zip = [];
        private string _checksum = "";

        public List<string> Requests { get; } = [];
        public Exception? Failure { get; set; }

        public void Publish(byte[] zip, string? checksum = null)
        {
            _zip = zip;
            _checksum = checksum ?? Convert.ToHexStringLower(SHA256.HashData(zip));
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure != null) throw Failure;

            var url = request.RequestUri!.ToString();
            Requests.Add(url);
            HttpContent? content = url == ZipUrl ? new ByteArrayContent(_zip)
                : url == ZipUrl + ".sha256" ? new StringContent($"{_checksum}  worker.zip\n")
                : null;
            return Task.FromResult(new HttpResponseMessage(content == null ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            {
                Content = content,
            });
        }
    }
}
