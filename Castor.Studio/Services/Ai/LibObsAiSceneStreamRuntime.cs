using CastorApplication.Models.Studio;
using CastorApplication.Services.Config;

namespace CastorApplication.Services.Ai;

/// <summary>Application-facing manager for one independent RTMP output per scene.</summary>
internal sealed class LibObsAiSceneStreamRuntime : IAiSceneStreamRuntime
{
    private readonly IIndependentSceneOutputRuntime _outputs;
    private readonly string _baseUrl;
    private readonly Dictionary<Guid, AiSceneStream> _active = [];
    private readonly object _gate = new();
    private bool _disposed;

    public bool IsAvailable => !_disposed && _outputs.IsAvailable;
    public string UnavailableMessage => _outputs.UnavailableMessage;
    public IReadOnlyDictionary<Guid, AiSceneStream> ActiveStreams
    {
        get { lock (_gate) return new Dictionary<Guid, AiSceneStream>(_active); }
    }

    public event EventHandler<AiSceneStreamStateChangedEventArgs>? StateChanged;

    public LibObsAiSceneStreamRuntime(
        IConfigService configService,
        IIndependentSceneOutputRuntime? outputs = null)
    {
        _baseUrl = AiSceneStreamConfiguration.BaseUrl(configService.Config.AiServer);
        _outputs = outputs ?? new UnavailableIndependentSceneOutputRuntime();
        _outputs.StateChanged += OnOutputStateChanged;
    }

    public async Task<IReadOnlyList<AiSceneStream>> StartStreamsAsync(
        IReadOnlyList<SceneDefinition> scenes,
        CancellationToken cancellationToken)
    {
        if (!IsAvailable) throw new InvalidOperationException(UnavailableMessage);
        var started = new List<AiSceneStream>();
        try
        {
            foreach (var scene in scenes.DistinctBy(scene => scene.Id))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (scene.Id == Guid.Empty) continue;
                lock (_gate)
                {
                    if (_active.ContainsKey(scene.Id)) continue;
                }

                var url = AiSceneStreamConfiguration.BuildPullUrl(_baseUrl, scene.Id);
                await _outputs.StartAsync(scene, url, cancellationToken);
                var stream = new AiSceneStream(scene.Id, scene.Name, url);
                lock (_gate) _active[scene.Id] = stream;
                started.Add(stream);
                StateChanged?.Invoke(this, new AiSceneStreamStateChangedEventArgs(
                    scene.Id, AiSceneStreamState.Started));
            }
            return started;
        }
        catch
        {
            if (started.Count > 0)
                await StopStreamsAsync(started.Select(stream => stream.SceneId).ToArray(), CancellationToken.None);
            throw;
        }
    }

    public async Task StopStreamsAsync(IReadOnlyCollection<Guid> sceneIds, CancellationToken cancellationToken)
    {
        foreach (var sceneId in sceneIds.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (!_active.ContainsKey(sceneId)) continue;
            }

            await _outputs.StopAsync(sceneId, cancellationToken);
            lock (_gate) _active.Remove(sceneId);
            StateChanged?.Invoke(this, new AiSceneStreamStateChangedEventArgs(
                sceneId, AiSceneStreamState.Stopped));
        }
    }

    public Task StopAllAsync(CancellationToken cancellationToken)
    {
        Guid[] ids;
        lock (_gate) ids = _active.Keys.ToArray();
        return StopStreamsAsync(ids, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed) return;
        try { StopAllAsync(CancellationToken.None).GetAwaiter().GetResult(); }
        catch { }
        _outputs.StateChanged -= OnOutputStateChanged;
        _outputs.Dispose();
        _disposed = true;
    }

    private void OnOutputStateChanged(object? sender, IndependentSceneOutputStateChangedEventArgs e)
    {
        lock (_gate)
        {
            if (e.State is IndependentSceneOutputState.Stopped or IndependentSceneOutputState.Error)
                _active.Remove(e.SceneId);
        }
        StateChanged?.Invoke(this, new AiSceneStreamStateChangedEventArgs(
            e.SceneId,
            e.State switch
            {
                IndependentSceneOutputState.Started => AiSceneStreamState.Started,
                IndependentSceneOutputState.Reconnecting => AiSceneStreamState.Reconnecting,
                IndependentSceneOutputState.Stopped => AiSceneStreamState.Stopped,
                _ => AiSceneStreamState.Error
            }, e.Message));
    }
}

internal enum IndependentSceneOutputState { Started, Reconnecting, Stopped, Error }

internal sealed class IndependentSceneOutputStateChangedEventArgs(
    Guid sceneId, IndependentSceneOutputState state, string message = "") : EventArgs
{
    public Guid SceneId { get; } = sceneId;
    public IndependentSceneOutputState State { get; } = state;
    public string Message { get; } = message;
}

/// <summary>Native seam for auxiliary scene outputs, with a LibObs implementation registered by default.</summary>
internal interface IIndependentSceneOutputRuntime : IDisposable
{
    bool IsAvailable { get; }
    string UnavailableMessage { get; }
    event EventHandler<IndependentSceneOutputStateChangedEventArgs>? StateChanged;
    Task StartAsync(SceneDefinition scene, string pushUrl, CancellationToken cancellationToken);
    Task StopAsync(Guid sceneId, CancellationToken cancellationToken);
}

internal sealed class UnavailableIndependentSceneOutputRuntime : IIndependentSceneOutputRuntime
{
    public bool IsAvailable => false;
    public string UnavailableMessage =>
        "Les sorties RTMP IA indépendantes nécessitent une extension LibObs auxiliaire.";
    public event EventHandler<IndependentSceneOutputStateChangedEventArgs>? StateChanged
    {
        add { }
        remove { }
    }
    public Task StartAsync(SceneDefinition scene, string pushUrl, CancellationToken cancellationToken) =>
        Task.FromException(new InvalidOperationException(UnavailableMessage));
    public Task StopAsync(Guid sceneId, CancellationToken cancellationToken) => Task.CompletedTask;
    public void Dispose() { }
}
