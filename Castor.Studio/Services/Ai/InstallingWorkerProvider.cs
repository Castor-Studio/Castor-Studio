using Castor.AI.Contracts.V1;
using Castor.AI.Providers;

namespace CastorApplication.Services.Ai;

/// <summary>
/// Installs the worker on the first start, then hands over to the provider that runs it. The
/// install progress goes through <see cref="StateChanged"/>, like the model downloads of the worker.
/// </summary>
internal sealed class InstallingWorkerProvider(AiWorkerInstaller installer, Func<string, IAiProvider> createWorker)
    : IAiProvider
{
    private IAiProvider? _worker;
    private string _workerPath = "";

    public string Name => "local";

    public event Action<AiConnectionState, string>? StateChanged;
    public event Action<WorkerCapabilities>? SessionStarted;
    public event Action<WorkerMessage>? MessageReceived;
    public event Action<string>? Log;

    public async Task StartAsync(AiProviderRequest request, CancellationToken cancellationToken = default)
    {
        string path;
        try
        {
            path = await installer.EnsureInstalledAsync(
                detail => StateChanged?.Invoke(AiConnectionState.Starting, detail), cancellationToken);
        }
        catch (Exception exception)
        {
            StateChanged?.Invoke(AiConnectionState.Faulted, exception.Message);
            throw;
        }

        if (_worker == null || path != _workerPath)
        {
            if (_worker != null) await _worker.DisposeAsync();
            _workerPath = path;
            _worker = createWorker(path);
            _worker.StateChanged += (state, detail) => StateChanged?.Invoke(state, detail);
            _worker.SessionStarted += capabilities => SessionStarted?.Invoke(capabilities);
            _worker.MessageReceived += message => MessageReceived?.Invoke(message);
            _worker.Log += line => Log?.Invoke(line);
        }

        await _worker.StartAsync(request, cancellationToken);
    }

    public bool Send(ClientMessage message) => _worker?.Send(message) ?? false;

    public bool PushAudio(ReadOnlySpan<float> interleaved, long ptsUs) =>
        _worker?.PushAudio(interleaved, ptsUs) ?? false;

    public bool PushVideo(string cameraId, ReadOnlySpan<byte> pixels, long ptsUs) =>
        _worker?.PushVideo(cameraId, pixels, ptsUs) ?? false;

    public Task StopAsync()
    {
        if (_worker != null) return _worker.StopAsync();
        StateChanged?.Invoke(AiConnectionState.Stopped, "stopped");
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => _worker?.DisposeAsync() ?? ValueTask.CompletedTask;
}
