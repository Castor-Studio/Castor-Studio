using CastorApplication.Models.Studio;

namespace CastorApplication.Services.Studio;

internal interface IStudioRuntime
{
    bool IsAvailable { get; }
    string UnavailableMessage { get; }

    Task<StudioRuntimeResult> StartPreviewAsync(SceneDefinition scene, CancellationToken cancellationToken);
    Task<StudioRuntimeResult> StopPreviewAsync(Guid sceneId, CancellationToken cancellationToken);
    Task<StudioRuntimeResult> StartStreamingAsync(StreamingRequest request, CancellationToken cancellationToken);
    Task<StudioRuntimeResult> StopStreamingAsync(CancellationToken cancellationToken);
}
