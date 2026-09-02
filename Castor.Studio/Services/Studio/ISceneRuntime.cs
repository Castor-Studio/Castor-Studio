using CastorApplication.Models.Studio;

namespace CastorApplication.Services.Studio;

internal sealed record SceneRuntimeResult(
    StudioRuntimeStatus Status,
    string EffectiveName = "",
    string Message = "")
{
    public bool IsSuccess => Status == StudioRuntimeStatus.Success;

    public static SceneRuntimeResult Success(string effectiveName = "") =>
        new(StudioRuntimeStatus.Success, effectiveName);

    public static SceneRuntimeResult Unavailable(string message) =>
        new(StudioRuntimeStatus.Unavailable, Message: message);

    public static SceneRuntimeResult Failure(string message) =>
        new(StudioRuntimeStatus.Failure, Message: message);
}

internal interface ISceneRuntime
{
    bool IsAvailable { get; }
    string UnavailableMessage { get; }

    SceneRuntimeResult CreateScene(Guid sceneId, string requestedName);
    SceneRuntimeResult RenameScene(Guid sceneId, string requestedName);
    SceneRuntimeResult RemoveScene(Guid sceneId);
}
