using Castor.Engine.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CastorApplication.Services.Ai;

public interface IAiAnalysisClient : IAsyncDisposable
{
    bool HasActiveSession { get; }
    string? SessionId { get; }

    event Action<AiSceneSwitchEvent>? SceneSwitchSuggested;
    event Action<AiSessionStatusEvent>? SessionStatusChanged;
    event Action<AiServerErrorEvent>? ServerErrorReceived;

    Task StartSessionAsync(
        string moduleName,
        IReadOnlyDictionary<string, string>? moduleConfig,
        CancellationToken cancellationToken);

    Task SendSourcesAsync(IReadOnlyList<SceneItem> scenes, CancellationToken cancellationToken);

    Task StopSessionAsync(string reason, CancellationToken cancellationToken);
}
