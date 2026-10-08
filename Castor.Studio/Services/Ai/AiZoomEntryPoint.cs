using CastorApplication.Models.Studio;
using CastorApplication.Services.Studio;

namespace CastorApplication.Services.Ai;

/// <summary>
/// Un zoom demandé par une IA. Les IA analysent des scènes : <see cref="SceneId"/> est toujours
/// connu. <see cref="SourceId"/> désigne la source à zoomer dans cette scène ; laissé vide, la
/// demande porte sur la scène entière.
/// </summary>
internal sealed record AiZoomRequest(Guid SceneId, Guid? SourceId, SourceZoom Zoom);

/// <summary>
/// Point d'entrée par lequel une IA dit comment zoomer. Le zoom passe par le moteur comme
/// celui de l'opérateur : même validation, même transformation confirmée en retour.
/// </summary>
internal interface IAiZoomEntryPoint
{
    SourceTransformResult Apply(AiZoomRequest request);
}

internal sealed class AiZoomEntryPoint(ISourceRuntime sourceRuntime) : IAiZoomEntryPoint
{
    public SourceTransformResult Apply(AiZoomRequest request)
    {
        // La façon de ramener le zoom d'une scène à ses sources (la source principale, celle
        // sous la zone visée, toutes ?) reste à décider : d'ici là, une IA désigne la source.
        if (request.SourceId is not { } sourceId)
            return SourceTransformResult.Failure(
                "Le zoom d'une scène entière n'est pas encore pris en charge : l'IA doit désigner une source.");

        return sourceRuntime.SetSourceZoom(request.SceneId, sourceId, request.Zoom);
    }
}
