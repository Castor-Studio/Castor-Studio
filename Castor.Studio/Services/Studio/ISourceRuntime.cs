using CastorApplication.Models.Studio;

namespace CastorApplication.Services.Studio;

internal sealed record SourceCatalog(
    IReadOnlyList<CaptureSourceOption> VideoSources,
    IReadOnlyList<AudioSourceOption> AudioSources,
    string Message = "");

internal abstract record SourceAddRequest(Guid SourceId, string RequestedName)
{
    public sealed record Video(Guid SourceId, string RequestedName, CaptureSourceOption Option)
        : SourceAddRequest(SourceId, RequestedName);

    public sealed record Audio(Guid SourceId, string RequestedName, AudioSourceOption Option)
        : SourceAddRequest(SourceId, RequestedName);

    public sealed record Media(Guid SourceId, string RequestedName, string FilePath, bool Loop)
        : SourceAddRequest(SourceId, RequestedName);
}

internal sealed record SourceRuntimeResult(
    StudioRuntimeStatus Status,
    string EffectiveName = "",
    string Message = "")
{
    public bool IsSuccess => Status == StudioRuntimeStatus.Success;

    public static SourceRuntimeResult Success(string effectiveName = "") =>
        new(StudioRuntimeStatus.Success, effectiveName);

    public static SourceRuntimeResult Unavailable(string message) =>
        new(StudioRuntimeStatus.Unavailable, Message: message);

    public static SourceRuntimeResult Failure(string message) =>
        new(StudioRuntimeStatus.Failure, Message: message);
}

internal sealed record SourceOrderResult(
    StudioRuntimeStatus Status,
    IReadOnlyList<Guid> SourceIds,
    string Message = "")
{
    public bool IsSuccess => Status == StudioRuntimeStatus.Success;

    public static SourceOrderResult Success(IReadOnlyList<Guid> sourceIds) =>
        new(StudioRuntimeStatus.Success, sourceIds);

    public static SourceOrderResult Unavailable(string message) =>
        new(StudioRuntimeStatus.Unavailable, [], message);

    public static SourceOrderResult Failure(string message) =>
        new(StudioRuntimeStatus.Failure, [], message);
}

internal sealed record SceneCompositionResult(
    StudioRuntimeStatus Status,
    SceneComposition Composition,
    string Message = "")
{
    public bool IsSuccess => Status == StudioRuntimeStatus.Success;

    public static SceneCompositionResult Success(SceneComposition composition) =>
        new(StudioRuntimeStatus.Success, composition);

    public static SceneCompositionResult Unavailable(string message) =>
        new(StudioRuntimeStatus.Unavailable, SceneComposition.Empty, message);

    public static SceneCompositionResult Failure(string message) =>
        new(StudioRuntimeStatus.Failure, SceneComposition.Empty, message);
}

internal interface ISourceRuntime
{
    bool IsAvailable { get; }
    string UnavailableMessage { get; }

    Task<SourceCatalog> EnumerateSourcesAsync(CancellationToken cancellationToken);
    SourceRuntimeResult AddSource(Guid sceneId, SourceAddRequest request);
    SourceRuntimeResult RemoveSource(Guid sceneId, Guid sourceId);
    SourceRuntimeResult SetMediaLoop(Guid sceneId, Guid sourceId, bool loop);

    /// <summary>
    /// Ordre d'empilement que le moteur détient pour cette scène, du premier plan vers
    /// l'arrière-plan. C'est la seule vérité sur l'ordre : rien ne doit le recalculer ailleurs.
    /// </summary>
    SourceOrderResult GetSourceOrder(Guid sceneId);

    /// <summary>
    /// Place une source au rang <paramref name="layerIndex"/> de cet ordre, où 0 est le
    /// premier plan. Le moteur reste seul à connaître sa propre numérotation interne.
    /// </summary>
    SourceRuntimeResult MoveSource(Guid sceneId, Guid sourceId, int layerIndex);

    /// <summary>
    /// Composition que le moteur rend pour cette scène : la taille de son canvas et la
    /// transformation de chaque source, dans l'ordre d'empilement (rang 0 = premier plan).
    /// Tout est lu d'un seul tenant, pour que l'interface dessine un état cohérent plutôt
    /// qu'un assemblage de lectures prises à des instants différents.
    /// </summary>
    SceneCompositionResult GetSceneComposition(Guid sceneId);
}
