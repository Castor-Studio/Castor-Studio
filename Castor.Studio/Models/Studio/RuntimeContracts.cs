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

public enum RecordingContainer
{
    Mp4,
    Mkv,
    WebM
}

public sealed record RecordingRequest(
    Guid SceneId,
    string OutputPath,
    int Fps,
    int VideoBitrateKbps,
    int AudioBitrateKbps,
    int AudioSampleRate,
    int AudioChannels,
    int BaseWidth,
    int BaseHeight,
    int OutputWidth,
    int OutputHeight,
    RecordingContainer Container);

public sealed record StreamingRequest(
    SceneDefinition Scene,
    StreamingPlatform Platform,
    string StreamKeyOrUrl,
    int Fps,
    int VideoBitrateKbps);
