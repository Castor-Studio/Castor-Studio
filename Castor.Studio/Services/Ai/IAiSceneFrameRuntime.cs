namespace CastorApplication.Services.Ai;

/// <summary>One raw frame of a scene, borrowed for the duration of the call.</summary>
internal delegate void AiSceneFrameSink(Guid sceneId, ReadOnlySpan<byte> bgra, int width, int height, int stride);

/// <summary>
/// Renders scenes on their own, without encoding, and hands every frame (BGRA) to a sink:
/// what the local AI reads for the domains that look at the picture (football).
/// </summary>
internal interface IAiSceneFrameRuntime : IDisposable
{
    bool IsAvailable { get; }
    string UnavailableMessage { get; }

    /// <summary>
    /// Renders exactly <paramref name="sceneIds"/> at <paramref name="width"/> x <paramref name="height"/>,
    /// at about <paramref name="fps"/> (0: every frame of the mix). Scenes already rendered that way keep
    /// running, the others are stopped. The sink runs on the libobs video thread and must not block.
    /// </summary>
    void Start(IReadOnlyCollection<Guid> sceneIds, int width, int height, double fps, AiSceneFrameSink sink);

    void StopAll();
}

internal sealed class UnavailableAiSceneFrameRuntime : IAiSceneFrameRuntime
{
    public bool IsAvailable => false;
    public string UnavailableMessage => "Le rendu des scènes pour l'IA nécessite le runtime LibObs.";
    public void Start(IReadOnlyCollection<Guid> sceneIds, int width, int height, double fps, AiSceneFrameSink sink) =>
        throw new InvalidOperationException(UnavailableMessage);
    public void StopAll() { }
    public void Dispose() { }
}
