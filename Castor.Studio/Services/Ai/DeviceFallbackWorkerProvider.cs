using Castor.AI.Contracts.V1;
using Castor.AI.Providers;
using CastorApplication.Models.Settings;

namespace CastorApplication.Services.Ai;

/// <summary>
/// Runs the worker on the device chosen in the settings, read on each start. On
/// <see cref="AiDevice.Auto"/> it uses the GPU, and starts the worker again on the CPU when
/// DirectML does not load the model in time. The worker's own fallback only covers a model
/// DirectML cannot load, not a slow load, which the SDK stops after its ready timeout.
/// </summary>
internal sealed class DeviceFallbackWorkerProvider(
    Func<string, IAiProvider> createWorker, string? adapterName, AiDeviceMemory memory,
    Func<AiDevice>? readDevice = null) : IAiProvider
{
    // Software adapter DXGI lists when there is no real GPU.
    internal const string BasicRenderDriver = "Microsoft Basic Render Driver";

    private IAiProvider? _worker;
    private AiDevice _mode;
    private string _device = "";
    private string _lastStartingDetail = "";
    private bool _gpuStarting;
    private string? _heldFault;

    public string Name => "local";

    public event Action<AiConnectionState, string>? StateChanged;
    public event Action<WorkerCapabilities>? SessionStarted;
    public event Action<WorkerMessage>? MessageReceived;
    public event Action<string>? Log;

    public async Task StartAsync(AiProviderRequest request, CancellationToken cancellationToken = default)
    {
        var mode = readDevice?.Invoke() ?? AiDevice.Auto;
        if (_worker == null || mode != _mode)
        {
            if (_worker is { } previous)
            {
                _worker = null;
                await previous.DisposeAsync();
            }
            _mode = mode;
            Use(mode switch
            {
                AiDevice.Cpu => "cpu",
                AiDevice.Gpu => "gpu",
                _ => PrefersCpu() ? "cpu" : "gpu",
            });
        }

        // A forced device starts as is: no fallback, no notice.
        if (_mode != AiDevice.Auto)
        {
            await _worker!.StartAsync(request, cancellationToken);
            return;
        }

        if (_device == "cpu")
        {
            Log?.Invoke(adapterName == null
                ? $"No usable GPU, {LocalAiAnalysisClient.GpuFallbackLog}"
                : $"GPU '{adapterName}' loads the models too slowly, {LocalAiAnalysisClient.GpuFallbackLog}");
            await _worker!.StartAsync(request, cancellationToken);
            return;
        }

        // A fault of the GPU attempt is held back: it is not one if the CPU takes over.
        _lastStartingDetail = "";
        _heldFault = null;
        _gpuStarting = true;
        try
        {
            await _worker!.StartAsync(request, cancellationToken);
            return;
        }
        catch (TimeoutException) when (!cancellationToken.IsCancellationRequested
            && _lastStartingDetail.StartsWith("loading", StringComparison.OrdinalIgnoreCase))
        {
        }
        catch
        {
            if (_heldFault is { } fault) StateChanged?.Invoke(AiConnectionState.Faulted, fault);
            throw;
        }
        finally
        {
            _gpuStarting = false;
        }

        var gpu = _worker!;
        _worker = null;
        await gpu.DisposeAsync();
        if (adapterName != null) memory.RememberCpu(adapterName);
        StateChanged?.Invoke(AiConnectionState.Starting, "GPU too slow, starting on the CPU");
        Use("cpu");
        await StartAsync(request, cancellationToken);
    }

    private bool PrefersCpu() =>
        adapterName == null
        || string.Equals(adapterName, BasicRenderDriver, StringComparison.OrdinalIgnoreCase)
        || memory.PrefersCpu(adapterName);

    private void Use(string device)
    {
        var worker = createWorker(device);
        _worker = worker;
        _device = device;

        // A replaced worker can still raise events while it shuts down: only the current one counts.
        worker.StateChanged += (state, detail) =>
        {
            if (worker != _worker) return;
            if (state == AiConnectionState.Starting) _lastStartingDetail = detail;
            if (state == AiConnectionState.Faulted && _gpuStarting)
            {
                _heldFault = detail;
                return;
            }
            StateChanged?.Invoke(state, detail);
        };
        worker.SessionStarted += capabilities =>
        {
            if (worker == _worker) SessionStarted?.Invoke(capabilities);
        };
        worker.MessageReceived += message =>
        {
            if (worker == _worker) MessageReceived?.Invoke(message);
        };
        worker.Log += line =>
        {
            if (worker == _worker) Log?.Invoke(line);
        };
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
