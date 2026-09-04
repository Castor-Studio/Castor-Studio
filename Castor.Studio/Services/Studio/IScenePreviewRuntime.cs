using System;
using CastorApplication.Models.Studio;

namespace CastorApplication.Services.Studio;

public interface IScenePreviewRuntime
{
    bool IsAvailable { get; }
    string UnavailableMessage { get; }

    event EventHandler? PreviewResetRequested;

    Task<StudioRuntimeResult> StartPreviewAsync(
        SceneDefinition scene,
        IntPtr windowHandle,
        uint width,
        uint height,
        CancellationToken cancellationToken);

    void ResizePreview(uint width, uint height);

    Task<StudioRuntimeResult> StopPreviewAsync(
        Guid sceneId,
        CancellationToken cancellationToken);
}
