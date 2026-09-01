namespace CastorApplication.Models.Studio;

public enum StudioRuntimeStatus
{
    Success,
    Unavailable,
    Failure
}

public sealed record StudioRuntimeResult(StudioRuntimeStatus Status, string Message = "")
{
    public bool IsSuccess => Status == StudioRuntimeStatus.Success;

    public static StudioRuntimeResult Success() => new(StudioRuntimeStatus.Success);
    public static StudioRuntimeResult Unavailable(string message) => new(StudioRuntimeStatus.Unavailable, message);
    public static StudioRuntimeResult Failure(string message) => new(StudioRuntimeStatus.Failure, message);
}

public sealed record RecordingRequest(
    SceneDefinition Scene,
    string OutputPath,
    int Fps,
    int VideoBitrateKbps,
    int OutputWidth,
    int OutputHeight,
    int QualityIndex,
    string Container);

public sealed record StreamingRequest(
    SceneDefinition Scene,
    StreamingPlatform Platform,
    string StreamKeyOrUrl,
    int Fps,
    int VideoBitrateKbps);
