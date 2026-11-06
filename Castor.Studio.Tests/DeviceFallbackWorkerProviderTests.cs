using Castor.AI;
using Castor.AI.Providers;
using CastorApplication.Models.Settings;
using CastorApplication.Services.Ai;

namespace Castor.Studio.Tests;

public sealed class DeviceFallbackWorkerProviderTests : IDisposable
{
    private const string Uhd = "Intel(R) UHD Graphics";
    private const string Loading = "loading football/checkpoint_5_416.onnx";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"castor-device-{Guid.NewGuid():N}");
    private readonly AiDeviceMemory _memory;
    private readonly FakeAiProvider _gpu = new();
    private readonly FakeAiProvider _cpu = new();
    private readonly List<string> _devices = [];
    private readonly List<(AiConnectionState State, string Detail)> _states = [];
    private readonly List<string> _logs = [];
    private AiDevice _mode = AiDevice.Auto;

    public DeviceFallbackWorkerProviderTests()
    {
        _memory = new AiDeviceMemory(Path.Combine(_directory, "ai-device.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task A_slow_gpu_load_restarts_on_the_cpu_and_is_remembered()
    {
        _gpu.StartProgress.Add(Loading);
        _gpu.StartFailure = new TimeoutException($"Worker stuck for 10 s at: {Loading}");
        var provider = Create(Uhd);

        await provider.StartAsync(Request);

        Assert.Equal(["gpu", "cpu"], _devices);
        Assert.True(_gpu.Disposed);
        Assert.True(_cpu.Running);
        Assert.DoesNotContain(_states, change => change.State == AiConnectionState.Faulted);
        Assert.Contains(_logs, line => line.Contains(LocalAiAnalysisClient.GpuFallbackLog));
        Assert.True(_memory.PrefersCpu(Uhd));
    }

    [Fact]
    public async Task A_remembered_gpu_is_not_tried_again()
    {
        _memory.RememberCpu(Uhd);

        await Create(Uhd).StartAsync(Request);

        Assert.Equal(["cpu"], _devices);
        Assert.Contains(_logs, line => line.Contains(LocalAiAnalysisClient.GpuFallbackLog));
    }

    [Theory]
    [InlineData(DeviceFallbackWorkerProvider.BasicRenderDriver)]
    [InlineData(null)]
    public async Task Without_a_real_gpu_the_cpu_is_used(string? adapter)
    {
        await Create(adapter).StartAsync(Request);

        Assert.Equal(["cpu"], _devices);
    }

    [Fact]
    public async Task A_gpu_that_loads_in_time_stays_on_the_gpu()
    {
        await Create(Uhd).StartAsync(Request);

        Assert.Equal(["gpu"], _devices);
        Assert.Empty(_logs);
        Assert.False(_memory.PrefersCpu(Uhd));
    }

    [Fact]
    public async Task A_timeout_while_downloading_is_not_a_slow_gpu()
    {
        _gpu.StartProgress.Add("downloading football/checkpoint_5_416.onnx 42 %");
        _gpu.StartFailure = new TimeoutException("Worker not ready after 10 s");

        await Assert.ThrowsAsync<TimeoutException>(() => Create(Uhd).StartAsync(Request));

        Assert.Equal(["gpu"], _devices);
        Assert.Contains(_states, change => change.State == AiConnectionState.Faulted);
        Assert.False(_memory.PrefersCpu(Uhd));
    }

    [Fact]
    public async Task Other_start_failures_are_not_retried()
    {
        _gpu.StartFailure = new FileNotFoundException("Castor.AI.Worker.exe not found.");

        await Assert.ThrowsAsync<FileNotFoundException>(() => Create(Uhd).StartAsync(Request));

        Assert.Equal(["gpu"], _devices);
    }

    [Fact]
    public async Task A_cancelled_start_is_not_retried()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        _gpu.StartProgress.Add(Loading);
        _gpu.StartFailure = new TimeoutException("Worker stuck");

        await Assert.ThrowsAsync<TimeoutException>(() => Create(Uhd).StartAsync(Request, cancelled.Token));

        Assert.Equal(["gpu"], _devices);
    }

    [Fact]
    public async Task A_forced_cpu_skips_the_gpu_without_a_notice()
    {
        _mode = AiDevice.Cpu;

        await Create(Uhd).StartAsync(Request);

        Assert.Equal(["cpu"], _devices);
        Assert.Empty(_logs);
    }

    [Fact]
    public async Task A_forced_gpu_does_not_fall_back()
    {
        _mode = AiDevice.Gpu;
        _memory.RememberCpu(Uhd);
        _gpu.StartProgress.Add(Loading);
        _gpu.StartFailure = new TimeoutException($"Worker stuck for 10 s at: {Loading}");

        await Assert.ThrowsAsync<TimeoutException>(() => Create(Uhd).StartAsync(Request));

        Assert.Equal(["gpu"], _devices);
        Assert.Contains(_states, change => change.State == AiConnectionState.Faulted);
    }

    [Fact]
    public async Task A_changed_setting_applies_on_the_next_start()
    {
        var provider = Create(Uhd);
        await provider.StartAsync(Request);

        _mode = AiDevice.Cpu;
        await provider.StartAsync(Request);

        Assert.Equal(["gpu", "cpu"], _devices);
        Assert.True(_gpu.Disposed);
        Assert.True(_cpu.Running);
    }

    [Fact]
    public void A_corrupt_memory_file_remembers_nothing()
    {
        var path = Path.Combine(_directory, "corrupt.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "{ not json");

        Assert.False(new AiDeviceMemory(path).PrefersCpu(Uhd));
    }

    private DeviceFallbackWorkerProvider Create(string? adapter)
    {
        var provider = new DeviceFallbackWorkerProvider(device =>
        {
            _devices.Add(device);
            return device == "gpu" ? _gpu : _cpu;
        }, adapter, _memory, () => _mode);
        provider.StateChanged += (state, detail) => _states.Add((state, detail));
        provider.Log += _logs.Add;
        return provider;
    }

    private static AiProviderRequest Request => new("football", [], 0, new Dictionary<string, string>(), () => 0);
}
