using CastorApplication.Models.Studio;

namespace CastorApplication.Services.Studio;

// Raised for one live: the others keep their own state.
internal sealed class StreamingStateChangedEventArgs(string providerId, bool isStreaming, string message = "") : EventArgs
{
    public string ProviderId { get; } = providerId;
    public bool IsStreaming { get; } = isStreaming;
    public string Message { get; } = message;
}

internal interface IStreamingRuntime
{
    bool IsAvailable { get; }
    string UnavailableMessage { get; }

    event EventHandler<StreamingStateChangedEventArgs>? StreamingStateChanged;

    // One result per destination, in the request's order.
    Task<IReadOnlyList<StreamingDestinationResult>> StartStreamingAsync(
        StreamingRequest request,
        CancellationToken cancellationToken);

    // Stops every live.
    Task<StudioRuntimeResult> StopStreamingAsync(CancellationToken cancellationToken);

    // Stops one platform's live; the others go on.
    Task<StudioRuntimeResult> StopStreamingAsync(string providerId, CancellationToken cancellationToken);

    // Re-points the running lives at another scene. StartStreamingAsync pins the OBS
    // program channel to the scene that was active then, so a global scene switch has to
    // update the running live outputs as well.
    StudioRuntimeResult SwitchStreamingScene(Guid sceneId);
}
