using LibObs;

namespace CastorApplication.Services.Studio;

internal sealed class LibObsSceneRuntime : ISceneRuntime, IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, ObsScene> _scenes = [];
    private bool _initialized;
    private bool _disposed;
    private string _unavailableMessage = "";

    public bool IsAvailable => _initialized && !_disposed;
    public string UnavailableMessage => _unavailableMessage;

    public LibObsSceneRuntime()
    {
        try
        {
            Obs.Startup();
            _initialized = true;
        }
        catch (Exception exception)
        {
            _unavailableMessage = $"LibObs n'a pas pu être initialisé : {exception.Message}";
            TryShutdownAfterFailedStartup();
        }
    }

    public SceneRuntimeResult CreateScene(Guid sceneId, string requestedName)
    {
        if (!TryValidateRequest(requestedName, out var failure)) return failure;

        lock (_gate)
        {
            if (_scenes.ContainsKey(sceneId))
                return SceneRuntimeResult.Failure("Cette scène existe déjà dans LibObs.");

            ObsScene? scene = null;
            try
            {
                scene = ObsScene.Create(requestedName.Trim());
                var effectiveName = scene.Name;
                _scenes.Add(sceneId, scene);
                scene = null;
                return SceneRuntimeResult.Success(effectiveName);
            }
            catch (Exception exception)
            {
                scene?.Dispose();
                return SceneRuntimeResult.Failure($"Création impossible dans LibObs : {exception.Message}");
            }
        }
    }

    public SceneRuntimeResult RenameScene(Guid sceneId, string requestedName)
    {
        if (!TryValidateRequest(requestedName, out var failure)) return failure;

        lock (_gate)
        {
            if (!_scenes.TryGetValue(sceneId, out var scene))
                return SceneRuntimeResult.Failure("Cette scène n'existe pas dans LibObs.");

            try
            {
                using var source = scene.Source;
                source.Name = requestedName.Trim();
                return SceneRuntimeResult.Success(scene.Name);
            }
            catch (Exception exception)
            {
                return SceneRuntimeResult.Failure($"Renommage impossible dans LibObs : {exception.Message}");
            }
        }
    }

    public SceneRuntimeResult RemoveScene(Guid sceneId)
    {
        if (!IsAvailable) return SceneRuntimeResult.Unavailable(UnavailableMessageForOperation());

        lock (_gate)
        {
            if (!_scenes.TryGetValue(sceneId, out var scene))
                return SceneRuntimeResult.Failure("Cette scène n'existe pas dans LibObs.");

            try
            {
                scene.Remove();
                scene.Dispose();
                _scenes.Remove(sceneId);
                return SceneRuntimeResult.Success();
            }
            catch (Exception exception)
            {
                return SceneRuntimeResult.Failure($"Suppression impossible dans LibObs : {exception.Message}");
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var scene in _scenes.Values)
                scene.Dispose();
            _scenes.Clear();

            if (_initialized)
            {
                Obs.Shutdown();
                _initialized = false;
            }
        }
    }

    private bool TryValidateRequest(string requestedName, out SceneRuntimeResult failure)
    {
        if (!IsAvailable)
        {
            failure = SceneRuntimeResult.Unavailable(UnavailableMessageForOperation());
            return false;
        }

        if (string.IsNullOrWhiteSpace(requestedName))
        {
            failure = SceneRuntimeResult.Failure("Le nom de la scène est obligatoire.");
            return false;
        }

        failure = SceneRuntimeResult.Success();
        return true;
    }

    private string UnavailableMessageForOperation() =>
        string.IsNullOrWhiteSpace(_unavailableMessage)
            ? "LibObs n'est pas disponible."
            : _unavailableMessage;

    private static void TryShutdownAfterFailedStartup()
    {
        try
        {
            if (Obs.IsInitialized) Obs.Shutdown();
        }
        catch
        {
            // Preserve the initialization failure; there are no managed OBS handles yet.
        }
    }
}
