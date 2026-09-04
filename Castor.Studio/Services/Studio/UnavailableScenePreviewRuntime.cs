using System;
using CastorApplication.Models.Studio;

namespace CastorApplication.Services.Studio;

internal sealed class UnavailableScenePreviewRuntime : IScenePreviewRuntime
{
    public const string Message = "La preview LibObs n'est pas disponible.";

    public bool IsAvailable => false;
    public string UnavailableMessage => Message;

    public event EventHandler? PreviewResetRequested
    {
        add { }
        remove { }
    }

    public Task<StudioRuntimeResult> StartPreviewAsync(
        SceneDefinition scene,
        IntPtr windowHandle,
        uint width,
        uint height,
        CancellationToken cancellationToken) =>
        Task.FromResult(StudioRuntimeResult.Unavailable(Message));

    public void ResizePreview(uint width, uint height)
    {
    }

    public Task<StudioRuntimeResult> StopPreviewAsync(Guid sceneId, CancellationToken cancellationToken) =>
        Task.FromResult(StudioRuntimeResult.Success());
}
