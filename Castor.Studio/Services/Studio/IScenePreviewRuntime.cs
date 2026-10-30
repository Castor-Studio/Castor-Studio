using System;
using CastorApplication.Models.Studio;

namespace CastorApplication.Services.Studio;

public interface IScenePreviewRuntime
{
    bool IsAvailable { get; }
    string UnavailableMessage { get; }

    event EventHandler? PreviewResetRequested;

    // windowHandle identifies which native surface is asking: the runtime can host one
    // native preview per window at once, so a Studio panel and the Scenes page (or two
    // detached panels) each get their own live display instead of fighting over one.
    Task<StudioRuntimeResult> StartPreviewAsync(
        SceneDefinition scene,
        IntPtr windowHandle,
        uint width,
        uint height,
        CancellationToken cancellationToken);

    void ResizePreview(IntPtr windowHandle, uint width, uint height);

    /// <summary>
    /// Has the engine paint, on this surface's picture, the outline of each composed source and the
    /// handles of the selected source. The engine draws them, in the same
    /// frame and projection as the scene: nothing else can sit above
    /// a native surface.
    /// </summary>
    /// <remarks>
    /// The overlay is kept as is and reread every frame: passing one the caller
    /// later changes would have it read while it changes.
    /// </remarks>
    void SetCompositionOverlay(IntPtr windowHandle, CompositionOverlay overlay);

    Task<StudioRuntimeResult> StopPreviewAsync(
        IntPtr windowHandle,
        Guid sceneId,
        CancellationToken cancellationToken);
}
