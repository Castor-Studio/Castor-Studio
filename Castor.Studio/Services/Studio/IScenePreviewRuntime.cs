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
    /// Has the frame of each composed source and the selected source's grab points painted on
    /// this surface's picture. The engine draws them, in the same image and projection as the
    /// scene: nothing else can sit on top of a native surface.
    /// </summary>
    /// <remarks>
    /// The overlay is kept as is and reread every frame: passing one the caller changes afterwards
    /// would have it read while it changes.
    /// </remarks>
    void SetCompositionOverlay(IntPtr windowHandle, CompositionOverlay overlay);

    Task<StudioRuntimeResult> StopPreviewAsync(
        IntPtr windowHandle,
        Guid sceneId,
        CancellationToken cancellationToken);
}
