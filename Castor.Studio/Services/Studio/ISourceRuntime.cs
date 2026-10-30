using System.Text;
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

    public sealed record Network(Guid SourceId, string RequestedName, string Url)
        : SourceAddRequest(SourceId, RequestedName)
    {
        // The URL usually carries the stream key: it must never end up in a log.
        protected override bool PrintMembers(StringBuilder builder)
        {
            if (base.PrintMembers(builder)) builder.Append(", ");
            builder.Append("Url = <redacted>");
            return true;
        }
    }
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

internal sealed record SourceTransformResult(
    StudioRuntimeStatus Status,
    SourceTransform? Transform,
    string Message = "")
{
    public bool IsSuccess => Status == StudioRuntimeStatus.Success;

    public static SourceTransformResult Success(SourceTransform transform) =>
        new(StudioRuntimeStatus.Success, transform);

    public static SourceTransformResult Unavailable(string message) =>
        new(StudioRuntimeStatus.Unavailable, null, message);

    public static SourceTransformResult Failure(string message) =>
        new(StudioRuntimeStatus.Failure, null, message);
}

internal interface ISourceRuntime
{
    bool IsAvailable { get; }
    string UnavailableMessage { get; }

    Task<SourceCatalog> EnumerateSourcesAsync(CancellationToken cancellationToken);
    SourceRuntimeResult AddSource(Guid sceneId, SourceAddRequest request);
    SourceRuntimeResult RemoveSource(Guid sceneId, Guid sourceId);
    SourceRuntimeResult SetMediaLoop(Guid sceneId, Guid sourceId, bool loop);
    SourceRuntimeResult RenameSource(Guid sceneId, Guid sourceId, string requestedName);

    /// <summary>
    /// Stacking order the engine holds for this scene, from foreground to
    /// background. This is the only truth about the order: nothing may recompute it elsewhere.
    /// </summary>
    SourceOrderResult GetSourceOrder(Guid sceneId);

    /// <summary>
    /// Places a source at rank <paramref name="layerIndex"/> of this order, where 0 is the
    /// foreground. Only the engine knows its own internal numbering.
    /// </summary>
    SourceRuntimeResult MoveSource(Guid sceneId, Guid sourceId, int layerIndex);

    /// <summary>
    /// Composition the engine returns for this scene: its canvas size and the
    /// transform of each source, in stacking order (rank 0 = foreground).
    /// Everything is read in one go, so the interface draws a consistent state rather
    /// than a mix of reads taken at different moments.
    /// </summary>
    SceneCompositionResult GetSceneComposition(Guid sceneId);

    /// <summary>
    /// Gives a source this placement — position, scale, crop — in one go, and
    /// returns the transform the engine then composes. This confirmed value, and
    /// not the requested one, describes what is rendered.
    /// </summary>
    /// <remarks>
    /// A refused placement touches nothing: the source keeps exactly the transform
    /// it had, never a mix of the old and the new.
    /// </remarks>
    SourceTransformResult SetSourceTransform(Guid sceneId, Guid sourceId, SourcePlacement placement);

    /// <summary>
    /// Zooms a source's picture inside its frame, without touching the frame itself, and returns the
    /// confirmed transform. The zoom then follows the source: moving, stretching or cropping
    /// the frame keeps it. <see cref="SourceZoom.None"/> restores the whole picture.
    /// </summary>
    /// <remarks>
    /// A refused zoom touches nothing, like a refused placement.
    /// </remarks>
    SourceTransformResult SetSourceZoom(Guid sceneId, Guid sourceId, SourceZoom zoom);
}
