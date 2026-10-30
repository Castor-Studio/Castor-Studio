using System.Collections.Frozen;
using System.Diagnostics;
using Castor.AI;
using Castor.AI.Providers;

namespace CastorApplication.Services.Ai;

/// <summary>
/// The AI runs on this machine: a Castor.AI worker started by <see cref="AiClient"/>. A camera
/// of the SDK is a scene, its id the scene Guid as "N".
/// </summary>
internal sealed class LocalAiAnalysisClient : IAiAnalysisClient
{
    // Written by the worker (OnnxSessionFactory) when DirectML cannot load a model.
    internal const string GpuFallbackLog = "falling back to CPU";

    private readonly AiClient _client;
    private readonly IAiSceneFrameRuntime _frames;
    private readonly IAiAudioCaptureRuntime _audio;
    private readonly SemaphoreSlim _sync = new(1, 1);

    private string _moduleName = "";
    private AiDecisionMode _mode;
    private FrozenDictionary<Guid, string> _cameraIds = FrozenDictionary<Guid, string>.Empty;
    private IReadOnlyDictionary<AiMicrophone, int> _microphones = new Dictionary<AiMicrophone, int>();
    private float _lastAcceptedConfidence;
    private string _lastStartingDetail = "";
    private bool _starting;
    private bool _gpuFallbackReported;
    private bool _disposed;

    public bool IsAvailable => !_disposed;
    public string UnavailableMessage => "L'IA locale a été arrêtée.";
    public bool HasActiveSession => _client.IsRunning;
    public IReadOnlySet<Guid> ActiveSceneIds { get; private set; } = new HashSet<Guid>();

    public event EventHandler<AiSceneSwitchEvent>? SceneSwitchSuggested;
    public event EventHandler<AiSceneSwitchEvent>? SceneSwitchRequested;
    public event EventHandler<AiSessionStatusEvent>? SessionStatusChanged;
    public event EventHandler<AiServerErrorEvent>? ServerErrorReceived;

    public LocalAiAnalysisClient(AiClient client, IAiSceneFrameRuntime? frames = null,
        IAiAudioCaptureRuntime? audio = null)
    {
        _client = client;
        _frames = frames ?? new UnavailableAiSceneFrameRuntime();
        _audio = audio ?? new UnavailableAiAudioCaptureRuntime();
        _client.SuggestionReceived += OnSuggestionReceived;
        _client.OnAirChanged += OnOnAirChanged;
        _client.StateChanged += OnStateChanged;
        _client.SessionStarted += OnSessionStarted;
        _client.LogReceived += OnLogReceived;
    }

    public async Task StartSessionAsync(string moduleName, string mode, IReadOnlyList<AiSceneSetup> scenes,
        Guid? onAirSceneId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        var decisionMode = mode switch
        {
            "agent" => AiDecisionMode.Suggest,
            "auto" => AiDecisionMode.Auto,
            _ => throw new ArgumentException("Le mode IA doit être 'agent' ou 'auto'.", nameof(mode)),
        };

        await _sync.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (HasActiveSession) return;

            _moduleName = moduleName;
            _mode = decisionMode;
            await OpenAsync(scenes, onAirSceneId, restart: false, cancellationToken);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task UpdateSourcesAsync(IReadOnlyList<AiSceneSetup> scenes, Guid? onAirSceneId,
        CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            if (!HasActiveSession) throw new InvalidOperationException("Aucune session IA active.");
            await OpenAsync(scenes, onAirSceneId, restart: true, cancellationToken);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task StopSessionAsync(string reason, CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            _frames.StopAll();
            _audio.StopAll();
            await _client.StopAsync();
            ClearSession();
        }
        finally
        {
            _sync.Release();
        }
    }

    public void ReportOnAir(Guid sceneId)
    {
        if (_cameraIds.TryGetValue(sceneId, out var cameraId)) _client.ReportOnAir(cameraId);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        try { await StopSessionAsync("desktop_dispose", CancellationToken.None); }
        catch { }
        _client.SuggestionReceived -= OnSuggestionReceived;
        _client.OnAirChanged -= OnOnAirChanged;
        _client.StateChanged -= OnStateChanged;
        _client.SessionStarted -= OnSessionStarted;
        _client.LogReceived -= OnLogReceived;
        _frames.Dispose();
        _audio.Dispose();
        _disposed = true;
    }

    private async Task OpenAsync(IReadOnlyList<AiSceneSetup> scenes, Guid? onAirSceneId, bool restart,
        CancellationToken cancellationToken)
    {
        var selected = scenes
            .Where(setup => setup.Scene.Id != Guid.Empty)
            .DistinctBy(setup => setup.Scene.Id)
            .ToList();
        if (selected.Count == 0)
            throw new InvalidOperationException("Sélectionnez au moins une scène avec une source vidéo.");

        var cameraIds = selected.ToFrozenDictionary(setup => setup.Scene.Id, setup => setup.Scene.Id.ToString("N"));

        // One channel per microphone, in scene order: scenes sharing a microphone share its channel.
        var microphones = new Dictionary<AiMicrophone, int>();
        foreach (var setup in selected)
        {
            if (setup.Microphone is { } microphone && !microphones.ContainsKey(microphone))
                microphones.Add(microphone, microphones.Count);
        }

        var session = new AiSessionOptions
        {
            Domain = _moduleName,
            Mode = _mode,
            Cameras = selected.Select(setup => new AiCamera(
                cameraIds[setup.Scene.Id],
                setup.Scene.Name,
                setup.Role.Trim(),
                setup.Microphone is { } microphone ? microphones[microphone] : -1)).ToList(),
            InitialCameraId = onAirSceneId is { } onAir && cameraIds.TryGetValue(onAir, out var initial)
                ? initial
                : null,
        };

        _frames.StopAll();
        _audio.StopAll();
        _starting = true;
        _lastStartingDetail = "";
        _gpuFallbackReported = false;
        try
        {
            if (restart) await _client.RestartAsync(session, cancellationToken);
            else await _client.StartAsync(session, cancellationToken);
        }
        catch (Exception exception)
        {
            ClearSession();
            throw new InvalidOperationException(DescribeStartFailure(exception), exception);
        }
        finally
        {
            _starting = false;
        }

        _cameraIds = cameraIds;
        _microphones = microphones;
        ActiveSceneIds = cameraIds.Keys.ToHashSet();
        RefreshFrames();
        StartAudio();
    }

    private void ClearSession()
    {
        _cameraIds = FrozenDictionary<Guid, string>.Empty;
        _microphones = new Dictionary<AiMicrophone, int>();
        ActiveSceneIds = new HashSet<Guid>();
    }

    // The SDK keeps its audio feed across worker restarts: started once per session.
    private void StartAudio()
    {
        if (_microphones.Count == 0) return;
        try
        {
            _audio.Start(_microphones, WriteAudio);
        }
        catch (Exception exception)
        {
            ServerErrorReceived?.Invoke(this, new AiServerErrorEvent(
                "AI_AUDIO_UNAVAILABLE", $"L'IA n'entend pas tous les micros : {exception.Message}", false));
        }
    }

    private void WriteAudio(int channel, ReadOnlySpan<float> mono, int sampleRate) =>
        _client.WriteAudio(channel, mono, sampleRate);

    // The video format is only known once the worker accepted the session, and is announced
    // again after an automatic restart.
    private void RefreshFrames()
    {
        if (!HasActiveSession || _client.VideoFormat is not { } format)
        {
            _frames.StopAll();
            return;
        }

        try
        {
            _frames.Start(_cameraIds.Keys, (int)format.Width, (int)format.Height, format.Fps, WriteFrame);
        }
        catch (Exception exception)
        {
            ServerErrorReceived?.Invoke(this, new AiServerErrorEvent(
                "AI_VIDEO_UNAVAILABLE", $"L'IA ne reçoit pas l'image des scènes : {exception.Message}", false));
        }
    }

    private void WriteFrame(Guid sceneId, ReadOnlySpan<byte> bgra, int width, int height, int stride)
    {
        if (_cameraIds.TryGetValue(sceneId, out var cameraId))
            _client.WriteVideo(cameraId, bgra, width, height, stride);
    }

    private string DescribeStartFailure(Exception exception)
    {
        if (exception is FileNotFoundException)
            return $"Worker IA introuvable. Réinstallez Castor Studio ou définissez CASTOR_WORKER_PATH. ({exception.Message})";
        if (_lastStartingDetail.StartsWith("downloading", StringComparison.OrdinalIgnoreCase))
            return $"Échec du téléchargement des modèles IA ({_lastStartingDetail}) : {exception.Message}";
        if (exception is TimeoutException)
            return $"Le worker IA ne répond pas : {exception.Message}";
        return $"Démarrage de l'IA impossible : {exception.Message}";
    }

    private void OnSuggestionReceived(object? sender, AiSuggestion suggestion)
    {
        // Auto mode: the arbiter decides, accepted cuts arrive through OnAirChanged.
        if (suggestion.Verdict is { } verdict)
        {
            if (verdict.Accepted) _lastAcceptedConfidence = suggestion.Confidence;
            return;
        }

        if (TryParseSceneId(suggestion.CameraId, out var sceneId))
            SceneSwitchSuggested?.Invoke(this, new AiSceneSwitchEvent(sceneId, suggestion.Confidence));
    }

    private void OnOnAirChanged(object? sender, AiOnAirChange change)
    {
        if (change.Source != AiOnAirSource.Ai) return;

        // Events are posted to the UI thread: an operator cut may have landed in between,
        // and switching back to the AI pick would undo it.
        if (_client.OnAirCameraId != change.CameraId) return;

        if (TryParseSceneId(change.CameraId, out var sceneId))
            SceneSwitchRequested?.Invoke(this, new AiSceneSwitchEvent(sceneId, _lastAcceptedConfidence));
    }

    private void OnStateChanged(object? sender, AiStateChange change)
    {
        if (change.State == AiConnectionState.Starting) _lastStartingDetail = change.Detail;

        SessionStatusChanged?.Invoke(this, new AiSessionStatusEvent(change.State, change.Detail));

        // A start failure is thrown by StartSessionAsync; this is the worker dying under a running
        // session. Not fatal: the SDK restarts it on its own.
        if (change.State == AiConnectionState.Faulted && !_starting && HasActiveSession)
            ServerErrorReceived?.Invoke(this, new AiServerErrorEvent(
                "AI_WORKER_FAULTED", $"Le worker IA s'est arrêté : {change.Detail}", false));
    }

    private void OnSessionStarted(object? sender, Castor.AI.Contracts.V1.WorkerCapabilities capabilities)
    {
        // The first start is handled by OpenAsync once StartAsync returns.
        if (!_starting) RefreshFrames();
    }

    private void OnLogReceived(object? sender, AiLogEntry entry)
    {
        Debug.WriteLine($"[Castor.AI {entry.Source}] {entry.Message}");

        if (!_gpuFallbackReported && entry.Message.Contains(GpuFallbackLog, StringComparison.OrdinalIgnoreCase))
        {
            _gpuFallbackReported = true;
            ServerErrorReceived?.Invoke(this, new AiServerErrorEvent(
                "AI_GPU_UNAVAILABLE", "GPU indisponible : l'IA tourne sur le CPU, plus lentement.", false));
        }
    }

    private static bool TryParseSceneId(string cameraId, out Guid sceneId) =>
        Guid.TryParseExact(cameraId, "N", out sceneId);
}
