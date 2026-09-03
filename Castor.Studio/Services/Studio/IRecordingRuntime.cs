using CastorApplication.Models.Studio;

namespace CastorApplication.Services.Studio;

internal sealed class RecordingStateChangedEventArgs(bool isRecording, string message = "") : EventArgs
{
    public bool IsRecording { get; } = isRecording;
    public string Message { get; } = message;
}

internal interface IRecordingRuntime
{
    bool IsAvailable { get; }
    string UnavailableMessage { get; }

    event EventHandler<RecordingStateChangedEventArgs>? StateChanged;

    Task<StudioRuntimeResult> StartRecordingAsync(RecordingRequest request, CancellationToken cancellationToken);
    Task<StudioRuntimeResult> StopRecordingAsync(CancellationToken cancellationToken);
}
