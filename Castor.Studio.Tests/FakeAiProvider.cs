using Castor.AI.Contracts.V1;
using Castor.AI.Providers;

namespace Castor.Studio.Tests;

/// <summary>Plays the worker behind a real AiClient: records what it is sent, raises what a worker would.</summary>
internal sealed class FakeAiProvider : IAiProvider
{
    public List<AiProviderRequest> Requests { get; } = [];
    public List<ClientMessage> Sent { get; } = [];
    public Dictionary<string, int> VideoFrames { get; } = [];
    public bool Running { get; private set; }

    /// <summary>Announced on start: no video by default, like the podcast worker.</summary>
    public WorkerCapabilities Capabilities { get; set; } = new() { WorkerId = "fake", Domain = "podcast" };

    /// <summary>Thrown by the next StartAsync, after the given progress lines.</summary>
    public Exception? StartFailure { get; set; }
    public List<string> StartProgress { get; } = [];

    public string Name => "fake";

    public event Action<AiConnectionState, string>? StateChanged;
    public event Action<WorkerCapabilities>? SessionStarted;
    public event Action<WorkerMessage>? MessageReceived;
    public event Action<string>? Log;

    public Task StartAsync(AiProviderRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        StateChanged?.Invoke(AiConnectionState.Starting, "Starting worker");
        foreach (var line in StartProgress) StateChanged?.Invoke(AiConnectionState.Starting, line);
        if (StartFailure is { } failure)
        {
            StartFailure = null;
            StateChanged?.Invoke(AiConnectionState.Faulted, failure.Message);
            return Task.FromException(failure);
        }

        Running = true;
        SessionStarted?.Invoke(Capabilities);
        StateChanged?.Invoke(AiConnectionState.Running, "running");
        return Task.CompletedTask;
    }

    public bool Send(ClientMessage message)
    {
        if (!Running) return false;
        Sent.Add(message);
        return true;
    }

    public bool PushAudio(ReadOnlySpan<float> interleaved, long ptsUs) => Running;

    public bool PushVideo(string cameraId, ReadOnlySpan<byte> pixels, long ptsUs)
    {
        VideoFrames[cameraId] = VideoFrames.GetValueOrDefault(cameraId) + 1;
        return Running;
    }

    public Task StopAsync()
    {
        Running = false;
        StateChanged?.Invoke(AiConnectionState.Stopped, "stopped");
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public IEnumerable<CameraOnAir> OnAirMessages() =>
        Sent.Where(message => message.BodyCase == ClientMessage.BodyOneofCase.OnAir).Select(message => message.OnAir);

    public void Decide(Guid sceneId, float confidence = 0.9f) =>
        MessageReceived?.Invoke(new WorkerMessage
        {
            Decision = new SceneDecision
            {
                DecisionId = Guid.NewGuid().ToString("N")[..8],
                CameraId = sceneId.ToString("N"),
                Confidence = confidence,
                Reason = "test",
            },
        });

    /// <summary>The worker died under a running session; the SDK restarts it.</summary>
    public void Crash(string reason)
    {
        StateChanged?.Invoke(AiConnectionState.Faulted, reason);
        StateChanged?.Invoke(AiConnectionState.Restarting, "Restarting in 1 s (attempt 1)");
    }

    public void Restarted()
    {
        SessionStarted?.Invoke(Capabilities);
        StateChanged?.Invoke(AiConnectionState.Running, "running");
    }

    public void WriteLog(string line) => Log?.Invoke(line);

    public static WorkerCapabilities FootballCapabilities() => new()
    {
        WorkerId = "fake",
        Domain = "football",
        RequiredInputs = { InputKind.Video },
        PreferredVideo = new VideoFormat { Width = 640, Height = 360, PixelFormat = PixelFormat.Bgra8, Fps = 15 },
    };
}
