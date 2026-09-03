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

internal interface ISourceRuntime
{
    bool IsAvailable { get; }
    string UnavailableMessage { get; }

    Task<SourceCatalog> EnumerateSourcesAsync(CancellationToken cancellationToken);
    SourceRuntimeResult AddSource(Guid sceneId, SourceAddRequest request);
    SourceRuntimeResult RemoveSource(Guid sceneId, Guid sourceId);
    SourceRuntimeResult SetMediaLoop(Guid sceneId, Guid sourceId, bool loop);
}
