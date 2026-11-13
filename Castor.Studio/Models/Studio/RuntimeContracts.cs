using System.Runtime.CompilerServices;
using CastorApplication.Services.Diagnostics;

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
    public static StudioRuntimeResult Failure(string message, [CallerMemberName] string operation = "")
    {
        AppLog.Warning($"{operation} failed: {message}");
        return new(StudioRuntimeStatus.Failure, message);
    }
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
    RecordingContainer Container,
    // Identifies the recording session in later calls and events; empty lets the runtime
    // pick one (stop-all and pause-all calls still reach it).
    Guid SessionId = default,
    // True: the session follows the program channel, re-pointed by SwitchRecordingScene.
    // False: a dedicated view renders SceneId alone, whatever the program shows.
    bool FollowsProgram = true);

// One live per destination, all showing Scene and sharing the same encoding settings.
public sealed record StreamingRequest(
    SceneDefinition Scene,
    IReadOnlyList<StreamingDestination> Destinations,
    int Fps,
    int VideoBitrateKbps,
    int AudioBitrateKbps,
    int AudioSampleRate,
    int AudioChannels);

// Each destination starts or fails on its own: a refused key leaves the other lives running.
public sealed record StreamingDestinationResult(string ProviderId, StudioRuntimeResult Result);
