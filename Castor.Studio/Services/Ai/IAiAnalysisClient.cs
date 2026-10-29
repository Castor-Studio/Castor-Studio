using CastorApplication.Models.Studio;

namespace CastorApplication.Services.Ai;

internal interface IAiAnalysisClient : IAsyncDisposable
{
    bool IsAvailable { get; }
    string UnavailableMessage { get; }
    bool HasActiveSession { get; }
    IReadOnlySet<Guid> ActiveSceneIds { get; }

    /// <summary>Agent mode: a scene the operator may put on air.</summary>
    event EventHandler<AiSceneSwitchEvent>? SceneSwitchSuggested;

    /// <summary>Auto mode: the AI took this scene, the app must switch to it.</summary>
    event EventHandler<AiSceneSwitchEvent>? SceneSwitchRequested;

    event EventHandler<AiSessionStatusEvent>? SessionStatusChanged;
    event EventHandler<AiServerErrorEvent>? ServerErrorReceived;

    /// <param name="mode">"agent" or "auto".</param>
    /// <param name="onAirSceneId">Scene on air now; used as the first shot when it is one of <paramref name="scenes"/>.</param>
    Task StartSessionAsync(string moduleName, string mode, IReadOnlyList<SceneDefinition> scenes,
        Guid? onAirSceneId, CancellationToken cancellationToken);

    /// <summary>Replaces the scenes of the running session. The AI starts again from scratch.</summary>
    Task UpdateSourcesAsync(IReadOnlyList<SceneDefinition> scenes, Guid? onAirSceneId,
        CancellationToken cancellationToken);

    Task StopSessionAsync(string reason, CancellationToken cancellationToken);

    /// <summary>
    /// The app put <paramref name="sceneId"/> on air. Call it on every cut, whoever made it:
    /// ignored when the AI already knows it is on air (its own cut in auto mode).
    /// </summary>
    void ReportOnAir(Guid sceneId);
}
