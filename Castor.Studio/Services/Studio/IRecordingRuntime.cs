using CastorApplication.Models.Studio;

namespace CastorApplication.Services.Studio;

internal sealed class RecordingStateChangedEventArgs(RecordingState state, string message = "") : EventArgs
{
    public RecordingState State { get; } = state;
    public string Message { get; } = message;
}

internal interface IRecordingRuntime
{
    bool IsAvailable { get; }
    string UnavailableMessage { get; }

    event EventHandler<RecordingStateChangedEventArgs>? StateChanged;

    Task<StudioRuntimeResult> StartRecordingAsync(RecordingRequest request, CancellationToken cancellationToken);
    Task<StudioRuntimeResult> StopRecordingAsync(CancellationToken cancellationToken);

    // Why the running recording cannot be paused (its container or encoder does not support
    // it), or "" when it can - or when nothing is recording.
    string PauseUnavailableReason { get; }

    // Pause and resume keep writing to the same file. Success means libobs accepted the
    // request; StateChanged then reports Paused / Recording.
    StudioRuntimeResult PauseRecording();
    StudioRuntimeResult ResumeRecording();

    // Re-points the running output at another scene. StartRecordingAsync pins the OBS
    // program channel to the scene that was active then, so switching scenes has to say so
    // here too - otherwise the file keeps showing the old scene while the preview moves on.
    StudioRuntimeResult SwitchRecordingScene(Guid sceneId);
}
