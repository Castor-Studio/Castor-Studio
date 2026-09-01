using CastorApplication.Models.Studio;

namespace CastorApplication.Services.Studio;

internal interface ISceneCollectionService
{
    Task SaveAsync(string path, IReadOnlyCollection<SceneDefinition> scenes, CancellationToken cancellationToken);
    Task<IReadOnlyList<SceneDefinition>> LoadAsync(string path, CancellationToken cancellationToken);
}
