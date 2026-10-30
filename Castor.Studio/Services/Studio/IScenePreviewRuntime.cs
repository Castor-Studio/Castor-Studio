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
    /// Fait peindre, sur l'image de cette surface, le cadre de chaque source composée et les
    /// points d'accroche de la source choisie. C'est le moteur qui les trace, dans la même
    /// image et la même projection que la scène : rien d'autre ne peut se poser au-dessus
    /// d'une surface native.
    /// </summary>
    /// <remarks>
    /// L'overlay est gardé tel quel et relu à chaque image : en passer un que l'appelant
    /// modifie ensuite le ferait lire pendant qu'il change.
    /// </remarks>
    void SetCompositionOverlay(IntPtr windowHandle, CompositionOverlay overlay);

    Task<StudioRuntimeResult> StopPreviewAsync(
        IntPtr windowHandle,
        Guid sceneId,
        CancellationToken cancellationToken);
}
