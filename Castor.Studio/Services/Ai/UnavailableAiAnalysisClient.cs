using CastorApplication.Models.Studio;

namespace CastorApplication.Services.Ai;

internal sealed class UnavailableAiAnalysisClient(
    string unavailableMessage = "L'IA locale n'est pas disponible.") : IAiAnalysisClient, IDisposable
{
    public bool IsAvailable => false;
    public string UnavailableMessage => unavailableMessage;
    public bool HasActiveSession => false;
    public IReadOnlySet<Guid> ActiveSceneIds => new HashSet<Guid>();

    public event EventHandler<AiSceneSwitchEvent>? SceneSwitchSuggested
    {
        add { }
        remove { }
    }
    public event EventHandler<AiSceneSwitchEvent>? SceneSwitchRequested
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
        Guid? onAirSceneId, CancellationToken cancellationToken) => Unavailable();
    public Task UpdateSourcesAsync(IReadOnlyList<SceneDefinition> scenes, Guid? onAirSceneId,
        CancellationToken cancellationToken) => Unavailable();
    public Task StopSessionAsync(string reason, CancellationToken cancellationToken) => Task.CompletedTask;
    public void ReportOnAir(Guid sceneId) { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public void Dispose() { }

    private Task Unavailable() => Task.FromException(new InvalidOperationException(UnavailableMessage));
}
