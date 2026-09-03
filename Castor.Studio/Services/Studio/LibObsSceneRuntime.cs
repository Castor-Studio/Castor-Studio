using CastorApplication.Models.Studio;
using LibObs;

namespace CastorApplication.Services.Studio;

internal sealed class LibObsSceneRuntime : ISceneRuntime, ISourceRuntime, IDisposable
{
    private sealed record NativeSource(ObsSource Source, ObsSceneItem Item, bool IsMedia);

    private readonly object _gate = new();
    private readonly Dictionary<Guid, ObsScene> _scenes = [];
    private readonly Dictionary<Guid, Dictionary<Guid, NativeSource>> _sources = [];
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
            Obs.ResetVideo(new ObsVideoSettings());
            Obs.ResetAudio(new ObsAudioSettings());
            Obs.LoadModules().EnsureSuccess();
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
                _sources.Add(sceneId, []);
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
                if (_sources.TryGetValue(sceneId, out var sources))
                {
                    foreach (var nativeSource in sources.Values)
                        RemoveNativeSource(nativeSource);
                    sources.Clear();
                }

                scene.Remove();
                scene.Dispose();
                _sources.Remove(sceneId);
                _scenes.Remove(sceneId);
                return SceneRuntimeResult.Success();
            }
            catch (Exception exception)
            {
                return SceneRuntimeResult.Failure($"Suppression impossible dans LibObs : {exception.Message}");
            }
        }
    }

    public Task<SourceCatalog> EnumerateSourcesAsync(CancellationToken cancellationToken) =>
        Task.Run(() => EnumerateSources(cancellationToken), cancellationToken);

    public SourceRuntimeResult AddSource(Guid sceneId, SourceAddRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsAvailable) return SourceRuntimeResult.Unavailable(UnavailableMessageForOperation());
        if (request.SourceId == Guid.Empty)
            return SourceRuntimeResult.Failure("L'identifiant de la source est obligatoire.");
        if (string.IsNullOrWhiteSpace(request.RequestedName))
            return SourceRuntimeResult.Failure("Le nom de la source est obligatoire.");

        lock (_gate)
        {
            if (!IsAvailable) return SourceRuntimeResult.Unavailable(UnavailableMessageForOperation());
            if (!_scenes.TryGetValue(sceneId, out var scene) || !_sources.TryGetValue(sceneId, out var sources))
                return SourceRuntimeResult.Failure("Cette scène n'existe pas dans LibObs.");
            if (sources.ContainsKey(request.SourceId))
                return SourceRuntimeResult.Failure("Cette source existe déjà dans LibObs.");

            ObsSource? source = null;
            ObsSceneItem? item = null;
            try
            {
                source = CreateNativeSource(request);
                item = scene.Add(source);
                var effectiveName = source.Name;
                sources.Add(request.SourceId, new NativeSource(source, item, request is SourceAddRequest.Media));
                source = null;
                item = null;
                return SourceRuntimeResult.Success(effectiveName);
            }
            catch (Exception exception)
            {
                TryRollbackSource(source, item);
                return SourceRuntimeResult.Failure($"Ajout impossible dans LibObs : {exception.Message}");
            }
        }
    }

    public SourceRuntimeResult RemoveSource(Guid sceneId, Guid sourceId)
    {
        if (!IsAvailable) return SourceRuntimeResult.Unavailable(UnavailableMessageForOperation());

        lock (_gate)
        {
            if (!IsAvailable) return SourceRuntimeResult.Unavailable(UnavailableMessageForOperation());
            if (!_sources.TryGetValue(sceneId, out var sources) || !sources.TryGetValue(sourceId, out var source))
                return SourceRuntimeResult.Failure("Cette source n'existe pas dans LibObs.");

            try
            {
                RemoveNativeSource(source);
                sources.Remove(sourceId);
                return SourceRuntimeResult.Success();
            }
            catch (Exception exception)
            {
                return SourceRuntimeResult.Failure($"Suppression impossible dans LibObs : {exception.Message}");
            }
        }
    }

    public SourceRuntimeResult SetMediaLoop(Guid sceneId, Guid sourceId, bool loop)
    {
        if (!IsAvailable) return SourceRuntimeResult.Unavailable(UnavailableMessageForOperation());

        lock (_gate)
        {
            if (!IsAvailable) return SourceRuntimeResult.Unavailable(UnavailableMessageForOperation());
            if (!_sources.TryGetValue(sceneId, out var sources) || !sources.TryGetValue(sourceId, out var source))
                return SourceRuntimeResult.Failure("Cette source n'existe pas dans LibObs.");
            if (!source.IsMedia)
                return SourceRuntimeResult.Failure("Seules les sources média peuvent être lues en boucle.");

            try
            {
                using var settings = new ObsData();
                settings.SetBool(ObsKnownSettings.Media.Looping, loop);
                source.Source.Update(settings);
                return SourceRuntimeResult.Success(source.Source.Name);
            }
            catch (Exception exception)
            {
                return SourceRuntimeResult.Failure($"Mise à jour impossible dans LibObs : {exception.Message}");
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var sceneSources in _sources.Values)
            {
                foreach (var nativeSource in sceneSources.Values)
                    DisposeNativeSource(nativeSource);
            }
            _sources.Clear();

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

    private SourceCatalog EnumerateSources(CancellationToken cancellationToken)
    {
        if (!IsAvailable) return new([], [], UnavailableMessageForOperation());

        lock (_gate)
        {
            if (!IsAvailable) return new([], [], UnavailableMessageForOperation());
            cancellationToken.ThrowIfCancellationRequested();
            var videos = new List<CaptureSourceOption>();
            var audio = new List<AudioSourceOption>();
            var failures = new List<string>();

            TryEnumerate("écrans", () =>
            {
                videos.AddRange(ObsSource.GetWindowsDisplayCaptureTargets().Select(target =>
                    new CaptureSourceOption(target.Id, target.DisplayName, VideoCaptureKind.Monitor, target.Id)));
            }, failures);
            TryEnumerate("fenêtres", () =>
            {
                videos.AddRange(ObsSource.GetPropertyListItems(
                    ObsKnownIds.Sources.WindowsWindowCapture,
                    ObsKnownSettings.WindowsWindowCapture.Window).Select(target =>
                    new CaptureSourceOption(target.Value, target.DisplayName, VideoCaptureKind.Window, target.Value)));
            }, failures);
            TryEnumerate("caméras", () =>
            {
                videos.AddRange(ObsSource.GetPropertyListItems(
                    ObsKnownIds.Sources.WindowsVideoCaptureDevice,
                    ObsKnownSettings.WindowsVideoCaptureDevice.VideoDeviceId).Select(target =>
                    new CaptureSourceOption(target.Value, target.DisplayName, VideoCaptureKind.Camera, target.Value)));
            }, failures);
            TryEnumerate("audio système", () =>
            {
                audio.AddRange(ObsSource.GetPropertyListItems(
                    ObsKnownIds.Sources.WindowsAudioOutputCapture,
                    ObsKnownSettings.WindowsAudioCapture.DeviceId).Select(target =>
                    new AudioSourceOption(target.Value, target.DisplayName, AudioCaptureKind.LoopbackGlobal, target.Value)));
            }, failures);
            TryEnumerate("microphones", () =>
            {
                audio.AddRange(ObsSource.GetPropertyListItems(
                    ObsKnownIds.Sources.WindowsAudioInputCapture,
                    ObsKnownSettings.WindowsAudioCapture.DeviceId).Select(target =>
                    new AudioSourceOption(target.Value, target.DisplayName, AudioCaptureKind.Microphone, target.Value)));
            }, failures);

            cancellationToken.ThrowIfCancellationRequested();
            return new(videos, audio, string.Join(" | ", failures));
        }
    }

    private static ObsSource CreateNativeSource(SourceAddRequest request) => request switch
    {
        SourceAddRequest.Video video => video.Option.Type switch
        {
            VideoCaptureKind.Monitor => ObsSource.CreateWindowsDisplayCapture(video.RequestedName,
                new ObsWindowsDisplayCaptureSettings { MonitorId = video.Option.Id }),
            VideoCaptureKind.Window => ObsSource.CreateWindowsWindowCapture(video.RequestedName,
                new ObsWindowsWindowCaptureSettings { Window = video.Option.Id }),
            VideoCaptureKind.Camera => ObsSource.CreateWindowsVideoCaptureDevice(video.RequestedName,
                new ObsWindowsVideoCaptureDeviceSettings { DeviceId = video.Option.Id }),
            _ => throw new NotSupportedException($"Le type vidéo '{video.Option.Type}' n'est pas pris en charge.")
        },
        SourceAddRequest.Audio audio => audio.Option.Type switch
        {
            AudioCaptureKind.LoopbackGlobal or AudioCaptureKind.LoopbackWindow =>
                ObsSource.CreateWindowsAudioOutputCapture(audio.RequestedName,
                    new ObsWindowsAudioCaptureSettings { DeviceId = audio.Option.Id }),
            AudioCaptureKind.Microphone or AudioCaptureKind.CameraMic =>
                ObsSource.CreateWindowsAudioInputCapture(audio.RequestedName,
                    new ObsWindowsAudioCaptureSettings { DeviceId = audio.Option.Id }),
            _ => throw new NotSupportedException($"Le type audio '{audio.Option.Type}' n'est pas pris en charge.")
        },
        SourceAddRequest.Media media => ObsSource.CreateMediaSource(media.RequestedName,
            new ObsMediaSourceSettings { FilePath = media.FilePath, Loop = media.Loop }),
        _ => throw new NotSupportedException("Ce type de source n'est pas pris en charge.")
    };

    private static void TryEnumerate(string category, Action enumerate, ICollection<string> failures)
    {
        try
        {
            enumerate();
        }
        catch (Exception exception)
        {
            failures.Add($"Énumération {category} impossible : {exception.Message}");
        }
    }

    private static void RemoveNativeSource(NativeSource nativeSource)
    {
        nativeSource.Item.Remove();
        nativeSource.Item.Dispose();
        nativeSource.Source.Remove();
        nativeSource.Source.Dispose();
    }

    private static void DisposeNativeSource(NativeSource nativeSource)
    {
        try
        {
            nativeSource.Item.Dispose();
        }
        finally
        {
            nativeSource.Source.Dispose();
        }
    }

    private static void TryRollbackSource(ObsSource? source, ObsSceneItem? item)
    {
        try
        {
            item?.Remove();
        }
        catch
        {
        }
        finally
        {
            item?.Dispose();
        }

        try
        {
            source?.Remove();
        }
        catch
        {
        }
        finally
        {
            source?.Dispose();
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
