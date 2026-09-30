using CastorApplication.Models.Studio;

namespace CastorApplication.Services.Ai;

internal sealed class UnavailableAiAnalysisClient : IAiAnalysisClient, IDisposable
{
    public bool IsAvailable => false;
    public string UnavailableMessage => "L'analyse IA nécessite le runtime LibObs des sorties indépendantes.";
    public bool HasActiveSession => false;
    public string? SessionId => null;
    public IReadOnlySet<Guid> ActiveSceneIds => new HashSet<Guid>();

    public event EventHandler<AiSceneSwitchEvent>? SceneSwitchSuggested
    {
        add { }
        remove { }
    }
    public event EventHandler<AiSessionStatusEvent>? SessionStatusChanged
    {
        add { }
        remove { }
    }
    public event EventHandler<AiServerErrorEvent>? ServerErrorReceived
    {
        add { }
        remove { }
    }

    public Task StartSessionAsync(string moduleName, string mode, IReadOnlyList<SceneDefinition> scenes,
        CancellationToken cancellationToken) => Unavailable();
    public Task UpdateSourcesAsync(IReadOnlyList<SceneDefinition> scenes, CancellationToken cancellationToken) => Unavailable();
    public Task StopSessionAsync(string reason, CancellationToken cancellationToken) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public void Dispose() { }

    private Task Unavailable() => Task.FromException(new InvalidOperationException(UnavailableMessage));
}
