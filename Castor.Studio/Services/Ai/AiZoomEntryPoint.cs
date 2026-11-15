using CastorApplication.Models.Studio;
using CastorApplication.Services.Studio;

namespace CastorApplication.Services.Ai;

/// <summary>
/// A zoom requested by an AI. AIs analyze scenes: <see cref="SceneId"/> is always known.
/// <see cref="SourceId"/> names the source to zoom in that scene; left empty, the request
/// applies to the whole scene.
/// </summary>
internal sealed record AiZoomRequest(Guid SceneId, Guid? SourceId, SourceZoom Zoom);

/// <summary>
/// Entry point through which an AI says how to zoom. The zoom goes through the engine like the
/// operator's: same validation, same confirmed transform in return.
/// </summary>
internal interface IAiZoomEntryPoint
{
    SourceTransformResult Apply(AiZoomRequest request);
}

internal sealed class AiZoomEntryPoint(ISourceRuntime sourceRuntime) : IAiZoomEntryPoint
{
    public SourceTransformResult Apply(AiZoomRequest request)
    {
        // How to bring a scene's zoom down to its sources (the main source, the one under the
        // aimed area, all of them?) is still to be decided: until then, an AI names the source.
        if (request.SourceId is not { } sourceId)
            return SourceTransformResult.Failure(
                "Le zoom d'une scène entière n'est pas encore pris en charge : l'IA doit désigner une source.");

        return sourceRuntime.SetSourceZoom(request.SceneId, sourceId, request.Zoom);
    }
}
