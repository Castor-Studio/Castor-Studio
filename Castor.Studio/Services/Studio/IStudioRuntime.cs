using CastorApplication.Models.Studio;

namespace CastorApplication.Services.Studio;

internal interface IStudioRuntime
{
    bool IsAvailable { get; }
    string UnavailableMessage { get; }

    Task<IReadOnlyList<CaptureSourceOption>> GetVideoSourcesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AudioSourceOption>> GetAudioSourcesAsync(CancellationToken cancellationToken);
    Task<StudioRuntimeResult> StartPreviewAsync(SceneDefinition scene, CancellationToken cancellationToken);
    Task<StudioRuntimeResult> StopPreviewAsync(Guid sceneId, CancellationToken cancellationToken);
    Task<StudioRuntimeResult> StartRecordingAsync(RecordingRequest request, CancellationToken cancellationToken);
    Task<StudioRuntimeResult> StopRecordingAsync(CancellationToken cancellationToken);
    Task<StudioRuntimeResult> StartStreamingAsync(StreamingRequest request, CancellationToken cancellationToken);
    Task<StudioRuntimeResult> StopStreamingAsync(CancellationToken cancellationToken);
}
