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

    // Re-points the running output at another scene. StartRecordingAsync pins the OBS
    // program channel to the scene that was active then, so switching scenes has to say so
    // here too - otherwise the file keeps showing the old scene while the preview moves on.
    StudioRuntimeResult SwitchRecordingScene(Guid sceneId);
}
