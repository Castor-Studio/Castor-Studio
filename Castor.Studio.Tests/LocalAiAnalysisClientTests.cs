using Castor.AI;
using Castor.AI.Providers;
using CastorApplication.Models.Studio;
using CastorApplication.Services.Ai;

namespace Castor.Studio.Tests;

public sealed class LocalAiAnalysisClientTests
{
    private readonly FakeAiProvider _provider = new();
    private readonly FakeFrameRuntime _frames = new();
    private readonly FakeAudioRuntime _audio = new();
    private readonly AiClient _sdk;
    private readonly LocalAiAnalysisClient _client;
    private readonly SceneDefinition _wide = new() { Name = "Plateau" };
    private readonly SceneDefinition _closeup = new() { Name = "Caméra" };

    public LocalAiAnalysisClientTests()
    {
        _sdk = new AiClient(_provider);
        _client = new LocalAiAnalysisClient(_sdk, _frames, _audio);
    }

    [Theory]
    [InlineData("agent", AiDecisionMode.Suggest)]
    [InlineData("auto", AiDecisionMode.Auto)]
    public async Task Modes_map_to_the_sdk(string mode, AiDecisionMode expected)
    {
        await StartAsync(mode);

        Assert.Equal(expected, _sdk.Session!.Mode);
    }

    [Fact]
    public async Task An_unknown_mode_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _client.StartSessionAsync("podcast", "off", Setups(_wide), null, CancellationToken.None));
    }

    [Fact]
    public async Task Scenes_become_cameras_keyed_by_their_guid()
    {
        await StartAsync("agent", onAir: _closeup.Id);

        var request = Assert.Single(_provider.Requests);
        Assert.Equal("podcast", request.Domain);
        Assert.Equal([_wide.Id.ToString("N"), _closeup.Id.ToString("N")], request.Cameras.Select(c => c.CameraId));
        Assert.Equal(["Plateau", "Caméra"], request.Cameras.Select(c => c.Label));
        Assert.Equal(_closeup.Id.ToString("N"), _sdk.OnAirCameraId);
        Assert.Equal(new HashSet<Guid> { _wide.Id, _closeup.Id }, _client.ActiveSceneIds);
    }

    [Fact]
    public async Task A_scene_on_air_outside_the_session_is_not_the_first_shot()
    {
        await StartAsync("agent", onAir: Guid.NewGuid());

        Assert.Equal(_wide.Id.ToString("N"), _sdk.OnAirCameraId);
    }

    [Fact]
    public async Task Agent_suggestions_name_the_scene_and_confidence()
    {
        await StartAsync("agent");
        AiSceneSwitchEvent? suggested = null;
        _client.SceneSwitchSuggested += (_, e) => suggested = e;

        _provider.Decide(_closeup.Id, 0.87f);

        Assert.Equal(new AiSceneSwitchEvent(_closeup.Id, 0.87f), suggested);
    }

    [Fact]
    public async Task A_manual_cut_is_reported_as_manual()
    {
        await StartAsync("agent");

        _client.ReportOnAir(_closeup.Id);

        var onAir = Assert.Single(_provider.OnAirMessages(), m => m.Manual);
        Assert.Equal(_closeup.Id.ToString("N"), onAir.CameraId);
    }

    [Fact]
    public async Task Auto_cut_is_requested_and_not_echoed_as_manual()
    {
        await StartAsync("auto");
        AiSceneSwitchEvent? requested = null;
        _client.SceneSwitchRequested += (_, e) =>
        {
            requested = e;
            // What MulticamViewModel does: switching the output reports the cut back.
            _client.ReportOnAir(e.SceneId);
        };

        _provider.Decide(_closeup.Id, 0.91f);

        Assert.Equal(new AiSceneSwitchEvent(_closeup.Id, 0.91f), requested);
        // A manual report would lock AI cuts for ManualLockMs.
        Assert.DoesNotContain(_provider.OnAirMessages(), m => m.Manual);
    }

    [Fact]
    public async Task Auto_mode_does_not_raise_suggestions()
    {
        await StartAsync("auto");
        var suggested = false;
        _client.SceneSwitchSuggested += (_, _) => suggested = true;

        _provider.Decide(_closeup.Id);

        Assert.False(suggested);
    }

    [Fact]
    public async Task Changing_the_scenes_restarts_the_worker_with_the_new_list()
    {
        await StartAsync("agent");
        var added = new SceneDefinition { Name = "Public" };

        await _client.UpdateSourcesAsync(Setups(_wide, added), _wide.Id, CancellationToken.None);

        Assert.Equal(2, _provider.Requests.Count);
        Assert.Equal([_wide.Id.ToString("N"), added.Id.ToString("N")],
            _provider.Requests[^1].Cameras.Select(c => c.CameraId));
        Assert.Equal(new HashSet<Guid> { _wide.Id, added.Id }, _client.ActiveSceneIds);
        Assert.True(_client.HasActiveSession);
    }

    [Fact]
    public async Task A_missing_worker_gives_a_readable_error()
    {
        _provider.StartFailure = new FileNotFoundException("Castor.AI.Worker.exe not found.");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync("agent"));

        Assert.StartsWith("Worker IA introuvable", error.Message);
        Assert.False(_client.HasActiveSession);
        Assert.Empty(_client.ActiveSceneIds);
    }

    [Fact]
    public async Task A_failed_model_download_is_named()
    {
        _provider.StartProgress.Add("downloading football/checkpoint_5_416.onnx 42 % (10.0 / 24.0 MB)");
        _provider.StartFailure = new InvalidOperationException("Worker exited during startup (code 1)");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync("agent"));

        Assert.StartsWith("Échec du téléchargement des modèles IA", error.Message);
        Assert.Contains("42 %", error.Message);
    }

    [Fact]
    public async Task Start_progress_is_forwarded()
    {
        var statuses = new List<AiSessionStatusEvent>();
        _client.SessionStatusChanged += (_, e) => statuses.Add(e);
        _provider.StartProgress.Add("downloading podcast/silero_vad.onnx 42 %");

        await StartAsync("agent");

        Assert.Contains(new AiSessionStatusEvent(AiConnectionState.Starting, "downloading podcast/silero_vad.onnx 42 %"), statuses);
    }

    [Fact]
    public async Task A_start_failure_is_not_also_reported_as_a_worker_crash()
    {
        var errors = new List<AiServerErrorEvent>();
        _client.ServerErrorReceived += (_, e) => errors.Add(e);
        _provider.StartFailure = new InvalidOperationException("boom");

        await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync("agent"));

        Assert.Empty(errors);
    }

    [Fact]
    public async Task A_worker_crash_is_reported_without_ending_the_session()
    {
        await StartAsync("auto");
        var errors = new List<AiServerErrorEvent>();
        _client.ServerErrorReceived += (_, e) => errors.Add(e);

        _provider.Crash("worker exited (code -1)");

        var error = Assert.Single(errors);
        Assert.False(error.IsFatal);
        Assert.Contains("worker exited", error.ErrorMessage);
        Assert.True(_client.HasActiveSession);
    }

    [Fact]
    public async Task Gpu_fallback_is_reported_once()
    {
        await StartAsync("agent");
        var errors = new List<AiServerErrorEvent>();
        _client.ServerErrorReceived += (_, e) => errors.Add(e);

        _provider.WriteLog("warn: Model a.onnx: GPU initialization failed, falling back to CPU: no adapter");
        _provider.WriteLog("warn: Model b.onnx: GPU initialization failed, falling back to CPU: no adapter");

        Assert.Equal("AI_GPU_UNAVAILABLE", Assert.Single(errors).ErrorCode);
    }

    [Fact]
    public async Task Podcast_renders_no_scene()
    {
        await StartAsync("agent");

        Assert.Null(_frames.Format);
    }

    [Fact]
    public async Task Football_renders_each_scene_at_the_worker_size()
    {
        _provider.Capabilities = FakeAiProvider.FootballCapabilities();

        await StartAsync("auto", module: "football");

        Assert.Equal((640, 360), _frames.Format);
        Assert.Equal(15, _frames.Fps);
        Assert.Equal(new HashSet<Guid> { _wide.Id, _closeup.Id }, _frames.Scenes);

        _frames.Push(_closeup.Id, 640, 360);

        Assert.Equal(1, _provider.VideoFrames.GetValueOrDefault(_closeup.Id.ToString("N")));
    }

    [Fact]
    public async Task Renders_come_back_after_a_worker_restart_and_stop_with_the_session()
    {
        _provider.Capabilities = FakeAiProvider.FootballCapabilities();
        await StartAsync("auto", module: "football");

        _provider.Crash("worker exited");
        _provider.Restarted();
        Assert.Equal((640, 360), _frames.Format);

        await _client.StopSessionAsync("test", CancellationToken.None);
        Assert.Null(_frames.Format);
        Assert.False(_client.HasActiveSession);
    }

    [Fact]
    public async Task Roles_and_microphones_reach_the_cameras()
    {
        var wideMic = new AiMicrophone(_wide.Id, Guid.NewGuid());
        var guestMic = new AiMicrophone(_closeup.Id, Guid.NewGuid());
        var host = new SceneDefinition { Name = "Animateur serré" };

        await _client.StartSessionAsync("podcast", "agent",
        [
            new AiSceneSetup(_wide, "host", wideMic),
            new AiSceneSetup(_closeup, " guest ", guestMic),
            // Same person, same microphone: same channel.
            new AiSceneSetup(host, "host_zoom", wideMic),
        ], null, CancellationToken.None);

        var request = Assert.Single(_provider.Requests);
        Assert.Equal(2, request.AudioChannels);
        Assert.Equal(["host", "guest", "host_zoom"], request.Cameras.Select(c => c.Role));
        Assert.Equal([0, 1, 0], request.Cameras.Select(c => c.AudioChannel));
        Assert.Equal(new Dictionary<AiMicrophone, int> { [wideMic] = 0, [guestMic] = 1 }, _audio.Channels);
    }

    [Fact]
    public async Task A_scene_without_microphone_has_no_channel_and_nothing_is_captured()
    {
        await StartAsync("agent");

        Assert.Equal([-1, -1], _provider.Requests[0].Cameras.Select(c => c.AudioChannel));
        Assert.Equal(0, _provider.Requests[0].AudioChannels);
        Assert.Null(_audio.Channels);
    }

    [Fact]
    public async Task Microphones_are_released_on_restart_and_stop()
    {
        var microphone = new AiMicrophone(_wide.Id, Guid.NewGuid());
        await _client.StartSessionAsync("podcast", "agent",
            [new AiSceneSetup(_wide, "host", microphone)], null, CancellationToken.None);

        // The sink writes to the SDK from any thread.
        _audio.Push(0, 48_000);

        await _client.UpdateSourcesAsync(Setups(_wide), null, CancellationToken.None);
        Assert.Null(_audio.Channels);

        await _client.UpdateSourcesAsync([new AiSceneSetup(_wide, "host", microphone)], null, CancellationToken.None);
        Assert.NotNull(_audio.Channels);

        await _client.StopSessionAsync("test", CancellationToken.None);
        Assert.Null(_audio.Channels);
    }

    [Fact]
    public async Task A_microphone_that_cannot_be_captured_is_reported()
    {
        _audio.Failure = "Un micro choisi pour l'IA n'existe plus dans sa scène.";
        var errors = new List<AiServerErrorEvent>();
        _client.ServerErrorReceived += (_, e) => errors.Add(e);

        await _client.StartSessionAsync("podcast", "agent",
            [new AiSceneSetup(_wide, "host", new AiMicrophone(_wide.Id, Guid.NewGuid()))], null, CancellationToken.None);

        var error = Assert.Single(errors);
        Assert.Equal("AI_AUDIO_UNAVAILABLE", error.ErrorCode);
        Assert.False(error.IsFatal);
        Assert.True(_client.HasActiveSession);
    }

    private Task StartAsync(string mode, Guid? onAir = null, string module = "podcast") =>
        _client.StartSessionAsync(module, mode, Setups(_wide, _closeup), onAir, CancellationToken.None);

    private static List<AiSceneSetup> Setups(params SceneDefinition[] scenes) =>
        scenes.Select(scene => new AiSceneSetup(scene)).ToList();

    private sealed class FakeAudioRuntime : IAiAudioCaptureRuntime
    {
        private AiAudioSink? _sink;

        public Dictionary<AiMicrophone, int>? Channels { get; private set; }
        public string? Failure { get; set; }

        public void Start(IReadOnlyDictionary<AiMicrophone, int> channels, AiAudioSink sink)
        {
            Channels = channels.ToDictionary();
            _sink = sink;
            if (Failure != null) throw new InvalidOperationException(Failure);
        }

        public void StopAll()
        {
            Channels = null;
            _sink = null;
        }

        public void Push(int channel, int sampleRate) => _sink?.Invoke(channel, new float[480], sampleRate);

        public void Dispose() { }
    }

    private sealed class FakeFrameRuntime : IAiSceneFrameRuntime
    {
        private AiSceneFrameSink? _sink;

        public (int Width, int Height)? Format { get; private set; }
        public double Fps { get; private set; }
        public HashSet<Guid> Scenes { get; } = [];
        public bool IsAvailable => true;
        public string UnavailableMessage => "";

        public void Start(IReadOnlyCollection<Guid> sceneIds, int width, int height, double fps, AiSceneFrameSink sink)
        {
            Scenes.Clear();
            Scenes.UnionWith(sceneIds);
            Format = (width, height);
            Fps = fps;
            _sink = sink;
        }

        public void StopAll()
        {
            Scenes.Clear();
            Format = null;
            _sink = null;
        }

        public void Push(Guid sceneId, int width, int height) =>
            _sink?.Invoke(sceneId, new byte[width * height * 4], width, height, width * 4);

        public void Dispose() { }
    }
}
