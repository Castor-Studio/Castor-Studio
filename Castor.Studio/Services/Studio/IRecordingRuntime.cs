using CastorApplication.Models.Studio;

namespace CastorApplication.Services.Studio;

internal sealed class RecordingStateChangedEventArgs(Guid sessionId, RecordingState state, string message = "") : EventArgs
{
    public Guid SessionId { get; } = sessionId;
    public RecordingState State { get; } = state;
    public string Message { get; } = message;
}

// Several recordings can run at once, one session each: at most one follows the program
// channel, the others each record a scene of their own (RecordingRequest.FollowsProgram).
internal interface IRecordingRuntime
{
    bool IsAvailable { get; }
    string UnavailableMessage { get; }

    // Raised per session.
    event EventHandler<RecordingStateChangedEventArgs>? StateChanged;

    Task<StudioRuntimeResult> StartRecordingAsync(RecordingRequest request, CancellationToken cancellationToken);

    // Each returns once the session's output has stopped and released its file.
    Task<StudioRuntimeResult> StopRecordingAsync(Guid sessionId, CancellationToken cancellationToken);
    Task<StudioRuntimeResult> StopRecordingAsync(CancellationToken cancellationToken);

    // Why the session cannot be paused (its container or encoder does not support it), or ""
    // when it can - or when the session is not running.
    string GetPauseUnavailableReason(Guid sessionId);

    // Pause and resume keep writing to the same file. Success means libobs accepted the
    // request; StateChanged then reports Paused / Recording.
    StudioRuntimeResult PauseRecording(Guid sessionId);
    StudioRuntimeResult ResumeRecording(Guid sessionId);

    // Re-points the program-following session at another scene. StartRecordingAsync pins the
    // OBS program channel to the scene that was active then, so switching scenes has to say
    // so here too - otherwise the file keeps showing the old scene while the preview moves
    // on. Sessions dedicated to a scene are not affected.
    StudioRuntimeResult SwitchRecordingScene(Guid sceneId);
}
