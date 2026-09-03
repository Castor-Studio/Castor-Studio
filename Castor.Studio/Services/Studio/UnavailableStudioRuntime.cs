using CastorApplication.Models.Studio;

namespace CastorApplication.Services.Studio;

internal sealed class UnavailableStudioRuntime : IStudioRuntime
{
    public const string Message = "LibObs n'est pas encore connecté.";

    public bool IsAvailable => false;
    public string UnavailableMessage => Message;

    public Task<StudioRuntimeResult> StartPreviewAsync(SceneDefinition scene, CancellationToken cancellationToken) => Unavailable();
    public Task<StudioRuntimeResult> StopPreviewAsync(Guid sceneId, CancellationToken cancellationToken) => Unavailable();
    public Task<StudioRuntimeResult> StartRecordingAsync(RecordingRequest request, CancellationToken cancellationToken) => Unavailable();
    public Task<StudioRuntimeResult> StopRecordingAsync(CancellationToken cancellationToken) => Unavailable();
    public Task<StudioRuntimeResult> StartStreamingAsync(StreamingRequest request, CancellationToken cancellationToken) => Unavailable();
    public Task<StudioRuntimeResult> StopStreamingAsync(CancellationToken cancellationToken) => Unavailable();

    private static Task<StudioRuntimeResult> Unavailable() =>
        Task.FromResult(StudioRuntimeResult.Unavailable(Message));
}
