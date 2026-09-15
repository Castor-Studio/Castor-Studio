using CastorApplication.Models.Settings;
using CastorApplication.Models.Studio;
using CastorApplication.Services.Settings;
using LibObs;

namespace CastorApplication.Services.Studio;

internal sealed class LibObsSceneRuntime : ISceneRuntime, ISourceRuntime, IRecordingRuntime, IStreamingRuntime, IScenePreviewRuntime, IAiSceneSourceProvider, IDisposable
{
    private const string FfmpegOutputId = "ffmpeg_output";
    private const string LibVpxVp9EncoderName = "libvpx-vp9";
    private const string LibOpusEncoderName = "libopus";
    private const string WinRtRuntimeRelativePath = "obs-runtime\\bin\\64bit\\libobs-winrt.dll";

    private sealed record NativeSource(
        ObsSource Source,
        ObsSceneItem Item,
        bool IsMedia,
        bool ProvidesVideo);

    // One native display per window that asked for a preview - a Studio panel and the
    // Scenes page (or two detached panels) can each hold their own live session instead of
    // taking turns on a single global one.
    private sealed class PreviewSession
    {
        public required ObsView View { get; init; }
        public required ObsSource SceneSource { get; init; }
        public required Guid SceneId { get; init; }
        public required uint CanvasWidth { get; init; }
        public required uint CanvasHeight { get; init; }
        public required ObsDisplay Display { get; init; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<Guid, ObsScene> _scenes = [];
    private readonly Dictionary<Guid, Dictionary<Guid, NativeSource>> _sources = [];
    private readonly Dictionary<IntPtr, PreviewSession> _previewSessions = [];
    private readonly SettingsService? _settingsService;
    private ObsOutput? _recordingOutput;
    private ObsEncoder? _recordingVideoEncoder;
    private ObsEncoder? _recordingAudioEncoder;
    private Guid? _recordingSceneId;
    private TaskCompletionSource<ObsOutputStateChangedEventArgs>? _recordingStarted;
    private TaskCompletionSource<ObsOutputStateChangedEventArgs>? _recordingStopped;
    private bool _recordingStopRequested;
    private ObsOutput? _streamingOutput;
    private ObsEncoder? _streamingVideoEncoder;
    private ObsEncoder? _streamingAudioEncoder;
    private ObsService? _streamingService;
    private Guid? _streamingSceneId;
    private TaskCompletionSource<ObsOutputStateChangedEventArgs>? _streamingStarted;
    private TaskCompletionSource<ObsOutputStateChangedEventArgs>? _streamingStopped;
    private bool _streamingStopRequested;
    private bool _initialized;
    private bool _disposed;
    private string _unavailableMessage = "";
    private ObsVideoSettings? _videoSettings;
    private ApplicationSettings? _pendingVideoSettings;

    public bool IsAvailable => _initialized && !_disposed;
    public string UnavailableMessage => _unavailableMessage;

    public ObsSource? AcquireSceneSource(Guid sceneId)
    {
        lock (_gate)
        {
            if (!IsAvailable || !_scenes.TryGetValue(sceneId, out var scene)) return null;
            return scene.Source;
        }
    }

    public event EventHandler<RecordingStateChangedEventArgs>? StateChanged;
    public event EventHandler<StreamingStateChangedEventArgs>? StreamingStateChanged;
    public event EventHandler? PreviewResetRequested;

    public LibObsSceneRuntime(SettingsService? settingsService = null)
    {
        _settingsService = settingsService;
        try
        {
            Obs.Startup();
            _videoSettings = CreatePreviewVideoSettings(settingsService?.Load() ?? new ApplicationSettings());
            Obs.ResetVideo(_videoSettings);
            Obs.ResetAudio(new ObsAudioSettings());
            Obs.LoadModules().EnsureSuccess();
            _initialized = true;
            if (_settingsService != null)
                _settingsService.SettingsSaved += OnSettingsSaved;
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
            if (_recordingSceneId == sceneId)
                return SceneRuntimeResult.Failure("Cette scène est utilisée par l'enregistrement en cours.");
            if (_streamingSceneId == sceneId)
                return SceneRuntimeResult.Failure("Cette scène est utilisée par le live en cours.");
            if (!_scenes.TryGetValue(sceneId, out var scene))
                return SceneRuntimeResult.Failure("Cette scène n'existe pas dans LibObs.");

            try
            {
                DisposePreviewSessionsForScene(sceneId);

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
                sources.Add(request.SourceId, new NativeSource(
                    source,
                    item,
                    request is SourceAddRequest.Media,
                    request is not SourceAddRequest.Audio));
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

    public SourceOrderResult GetSourceOrder(Guid sceneId)
    {
        if (!IsAvailable) return SourceOrderResult.Unavailable(UnavailableMessageForOperation());

        lock (_gate)
        {
            if (!IsAvailable) return SourceOrderResult.Unavailable(UnavailableMessageForOperation());
            if (!_scenes.TryGetValue(sceneId, out var scene) || !_sources.TryGetValue(sceneId, out var sources))
                return SourceOrderResult.Failure("Cette scène n'existe pas dans LibObs.");

            try
            {
                return SourceOrderResult.Success(ReadLayerOrder(scene, sources));
            }
            catch (Exception exception)
            {
                return SourceOrderResult.Failure($"Lecture de l'ordre impossible dans LibObs : {exception.Message}");
            }
        }
    }

    public SourceRuntimeResult MoveSource(Guid sceneId, Guid sourceId, int layerIndex)
    {
        if (!IsAvailable) return SourceRuntimeResult.Unavailable(UnavailableMessageForOperation());
        if (layerIndex < 0) return SourceRuntimeResult.Failure("Le rang d'une source ne peut pas être négatif.");

        lock (_gate)
        {
            if (!IsAvailable) return SourceRuntimeResult.Unavailable(UnavailableMessageForOperation());
            if (!_sources.TryGetValue(sceneId, out var sources) || !sources.TryGetValue(sourceId, out var source))
                return SourceRuntimeResult.Failure("Cette source n'existe pas dans LibObs.");
            if (layerIndex >= sources.Count)
                return SourceRuntimeResult.Failure("Ce rang dépasse le nombre de sources de la scène.");

            try
            {
                // libobs numérote ses scene items de l'arrière-plan (0) vers le premier plan,
                // soit l'inverse du rang manipulé par l'opérateur. Le compte suivi ici est
                // tenu en phase avec la scène native par AddSource/RemoveSource, sous ce verrou.
                source.Item.SetOrderPosition(sources.Count - 1 - layerIndex);
                return SourceRuntimeResult.Success(source.Source.Name);
            }
            catch (Exception exception)
            {
                return SourceRuntimeResult.Failure($"Réordonnancement impossible dans LibObs : {exception.Message}");
            }
        }
    }

    // libobs énumère ses items de l'arrière-plan vers le premier plan ; on rend l'inverse, et
    // traduit en identifiants applicatifs pour qu'aucun handle natif ne sorte du runtime.
    private static IReadOnlyList<Guid> ReadLayerOrder(ObsScene scene, Dictionary<Guid, NativeSource> sources)
    {
        var sourceIdsByItemId = new Dictionary<long, Guid>(sources.Count);
        foreach (var (sourceId, native) in sources)
            sourceIdsByItemId[native.Item.Id] = sourceId;

        var items = scene.GetItems();
        try
        {
            var ordered = new List<Guid>(items.Count);
            for (var index = items.Count - 1; index >= 0; index--)
            {
                if (sourceIdsByItemId.TryGetValue(items[index].Id, out var sourceId))
                    ordered.Add(sourceId);
            }

            return ordered;
        }
        finally
        {
            // GetItems() prend une référence sur chaque item : à nous de les relâcher.
            foreach (var item in items) item.Dispose();
        }
    }

    private void OnSettingsSaved(object? sender, EventArgs e)
    {
        var settings = _settingsService?.Load();
        if (settings == null) return;

        lock (_gate)
        {
            if (!IsAvailable) return;
            if (_recordingOutput != null || _streamingOutput != null)
            {
                _pendingVideoSettings = settings;
                return;
            }

            ApplyVideoSettingsCore(settings);
        }
    }

    private void ApplyVideoSettingsCore(ApplicationSettings settings)
    {
        var next = CreatePreviewVideoSettings(settings);
        if (AreSameVideoSettings(_videoSettings, next))
        {
            _pendingVideoSettings = null;
            return;
        }

        _pendingVideoSettings = null;
        // Every live session renders against this canvas; resetting it invalidates all of
        // them at once, not just one.
        DisposeAllPreviewSessions();
        try
        {
            Obs.ResetVideo(next);
            _videoSettings = next;
        }
        catch
        {
            // The next preview start reports the LibObs error while keeping the
            // runtime alive for existing scenes and sources.
        }

        PreviewResetRequested?.Invoke(this, EventArgs.Empty);
    }

    private static ObsVideoSettings CreatePreviewVideoSettings(ApplicationSettings settings)
    {
        var (baseWidth, baseHeight) = VideoResolution.BaseFromIndex(settings.SelectedBaseResolutionIndex);
        var (outputWidth, outputHeight) = VideoResolution.OutputFromIndex(settings.SelectedOutputResolutionIndex);
        var fps = settings.SelectedFpsIndex switch
        {
            0 => 60,
            2 => 25,
            _ => 30
        };

        return new ObsVideoSettings
        {
            FpsNumerator = (uint)fps,
            BaseWidth = (uint)baseWidth,
            BaseHeight = (uint)baseHeight,
            OutputWidth = (uint)outputWidth,
            OutputHeight = (uint)outputHeight,
        };
    }

    private static bool AreSameVideoSettings(ObsVideoSettings? left, ObsVideoSettings right) =>
        left != null &&
        left.FpsNumerator == right.FpsNumerator &&
        left.FpsDenominator == right.FpsDenominator &&
        left.BaseWidth == right.BaseWidth &&
        left.BaseHeight == right.BaseHeight &&
        left.OutputWidth == right.OutputWidth &&
        left.OutputHeight == right.OutputHeight;

    public Task<StudioRuntimeResult> StartPreviewAsync(
        SceneDefinition scene,
        IntPtr windowHandle,
        uint width,
        uint height,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scene);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
            return Task.FromResult(StudioRuntimeResult.Unavailable("La preview LibObs nécessite Windows."));
        if (windowHandle == IntPtr.Zero)
            return Task.FromResult(StudioRuntimeResult.Failure("Le handle de la surface de preview est invalide."));

        lock (_gate)
        {
            if (!IsAvailable) return Task.FromResult(StudioRuntimeResult.Unavailable(UnavailableMessageForOperation()));
            if (!_scenes.TryGetValue(scene.Id, out var nativeScene))
                return Task.FromResult(StudioRuntimeResult.Failure("Cette scène n'existe pas dans LibObs."));
            try
            {
                if (_previewSessions.TryGetValue(windowHandle, out var existing) && existing.SceneId == scene.Id)
                {
                    existing.Display.Resize(Math.Max(1u, width), Math.Max(1u, height));
                    return Task.FromResult(StudioRuntimeResult.Success());
                }

                // A different scene for this window, or its first preview: replace only
                // this window's session, leaving every other window's session untouched.
                DisposePreviewSession(windowHandle);

                var sceneSource = nativeScene.Source;
                var view = ObsView.Create();
                view.SetSource(0, sceneSource);

                var display = ObsDisplay.Create(new ObsDisplaySettings
                {
                    WindowHandle = windowHandle,
                    Width = Math.Max(1u, width),
                    Height = Math.Max(1u, height),
                    BackgroundColor = 0xFF000000
                });

                var session = new PreviewSession
                {
                    View = view,
                    SceneSource = sceneSource,
                    SceneId = scene.Id,
                    CanvasWidth = _videoSettings?.BaseWidth ?? 1920,
                    CanvasHeight = _videoSettings?.BaseHeight ?? 1080,
                    Display = display,
                };
                display.AddRenderCallback(frame => RenderPreviewFrame(frame, session));
                _previewSessions[windowHandle] = session;

                return Task.FromResult(StudioRuntimeResult.Success());
            }
            catch (Exception exception)
            {
                DisposePreviewSession(windowHandle);
                return Task.FromResult(StudioRuntimeResult.Failure(
                    $"Démarrage de la preview impossible : {exception.Message}"));
            }
        }
    }

    public void ResizePreview(IntPtr windowHandle, uint width, uint height)
    {
        if (width == 0 || height == 0) return;

        lock (_gate)
        {
            if (!IsAvailable || !_previewSessions.TryGetValue(windowHandle, out var session)) return;
            try
            {
                session.Display.Resize(width, height);
            }
            catch
            {
                DisposePreviewSession(windowHandle);
            }
        }
    }

    public Task<StudioRuntimeResult> StopPreviewAsync(IntPtr windowHandle, Guid sceneId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_previewSessions.TryGetValue(windowHandle, out var session) && session.SceneId == sceneId)
                DisposePreviewSession(windowHandle);
        }

        return Task.FromResult(StudioRuntimeResult.Success());
    }

    public async Task<StudioRuntimeResult> StartRecordingAsync(
        RecordingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Task<ObsOutputStateChangedEventArgs> startedTask;

        lock (_gate)
        {
            if (!IsAvailable) return StudioRuntimeResult.Unavailable(UnavailableMessageForOperation());
            if (_recordingOutput != null)
                return StudioRuntimeResult.Failure("Un enregistrement est déjà en cours.");
            if (_streamingOutput != null)
                return StudioRuntimeResult.Failure("Arrêtez le live avant de démarrer l'enregistrement.");
            if (!_scenes.TryGetValue(request.SceneId, out var scene))
                return StudioRuntimeResult.Failure("Cette scène n'existe pas dans LibObs.");
            if (!_sources.TryGetValue(request.SceneId, out var sources) ||
                !sources.Values.Any(source => source.ProvidesVideo))
                return StudioRuntimeResult.Failure(
                    "La scène doit contenir au moins une source vidéo ou média.");

            var validationError = ValidateRecordingRequest(request);
            if (validationError.Length > 0) return StudioRuntimeResult.Failure(validationError);

            try
            {
                EnsureVideoSettingsForRecording(request);
                ConfigureRecordingMedia(request);
                using (var sceneSource = scene.Source)
                    Obs.SetOutputSource(0, sceneSource);

                var resources = request.Container == RecordingContainer.WebM
                    ? CreateWebMOutput(request)
                    : CreateMuxerOutput(request);

                _recordingOutput = resources.Output;
                _recordingVideoEncoder = resources.VideoEncoder;
                _recordingAudioEncoder = resources.AudioEncoder;
                _recordingSceneId = request.SceneId;
                _recordingStarted = NewOutputSignal();
                _recordingStopped = NewOutputSignal();
                _recordingOutput.StateChanged += OnRecordingOutputStateChanged;
                startedTask = _recordingStarted.Task;
                _recordingOutput.Start();
            }
            catch (Exception exception)
            {
                ReleaseRecordingResourcesCore();
                return StudioRuntimeResult.Failure($"Démarrage de l'enregistrement impossible : {exception.Message}");
            }
        }

        try
        {
            var state = await startedTask.WaitAsync(cancellationToken);
            return state.State == ObsOutputState.Started
                ? StudioRuntimeResult.Success()
                : StudioRuntimeResult.Failure(RecordingStopMessage(state));
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                try
                {
                    _recordingOutput?.ForceStop();
                }
                catch
                {
                }
                ReleaseRecordingResourcesCore();
            }
            throw;
        }
    }

    public StudioRuntimeResult SwitchRecordingScene(Guid sceneId)
    {
        lock (_gate)
        {
            if (!IsAvailable) return StudioRuntimeResult.Unavailable(UnavailableMessageForOperation());
            // Nothing running: StartRecordingAsync sets the program channel itself, from
            // whichever scene is active when the user hits record.
            if (_recordingOutput == null) return StudioRuntimeResult.Success();
            if (_recordingSceneId == sceneId) return StudioRuntimeResult.Success();
            if (!_scenes.TryGetValue(sceneId, out var scene))
                return StudioRuntimeResult.Failure("Cette scène n'existe pas dans LibObs.");

            try
            {
                using (var sceneSource = scene.Source)
                    Obs.SetOutputSource(0, sceneSource);
                // Keeps the "scene in use by the recording" guard on the scene actually
                // being recorded now, so the previous one becomes deletable again.
                _recordingSceneId = sceneId;
                return StudioRuntimeResult.Success();
            }
            catch (Exception exception)
            {
                return StudioRuntimeResult.Failure($"Changement de scène impossible : {exception.Message}");
            }
        }
    }

    public async Task<StudioRuntimeResult> StopRecordingAsync(CancellationToken cancellationToken)
    {
        Task<ObsOutputStateChangedEventArgs> stoppedTask;
        ObsOutput output;
        lock (_gate)
        {
            if (_recordingOutput == null || _recordingStopped == null)
                return StudioRuntimeResult.Success();

            output = _recordingOutput;
            stoppedTask = _recordingStopped.Task;
            try
            {
                _recordingStopRequested = true;
                output.Stop();
            }
            catch (Exception exception)
            {
                _recordingStopRequested = false;
                return StudioRuntimeResult.Failure($"Arrêt de l'enregistrement impossible : {exception.Message}");
            }
        }

        ObsOutputStateChangedEventArgs state;
        try
        {
            state = await stoppedTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                if (ReferenceEquals(output, _recordingOutput)) ReleaseRecordingResourcesCore();
            }
            throw;
        }
        lock (_gate)
        {
            if (ReferenceEquals(output, _recordingOutput)) ReleaseRecordingResourcesCore();
        }
        return state.StopCode is null or ObsOutputStopCode.Success
            ? StudioRuntimeResult.Success()
            : StudioRuntimeResult.Failure(RecordingStopMessage(state));
    }

    public async Task<StudioRuntimeResult> StartStreamingAsync(
        StreamingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Task<ObsOutputStateChangedEventArgs> startedTask;

        lock (_gate)
        {
            if (!IsAvailable) return StudioRuntimeResult.Unavailable(UnavailableMessageForOperation());
            if (_streamingOutput != null)
                return StudioRuntimeResult.Failure("Un live est déjà en cours.");
            if (_recordingOutput != null)
                return StudioRuntimeResult.Failure("Arrêtez l'enregistrement avant de lancer le live.");
            if (!_scenes.TryGetValue(request.Scene.Id, out var scene))
                return StudioRuntimeResult.Failure("Cette scène n'existe pas dans LibObs.");
            if (!_sources.TryGetValue(request.Scene.Id, out var sources) ||
                !sources.Values.Any(source => source.ProvidesVideo))
                return StudioRuntimeResult.Failure(
                    "La scène doit contenir au moins une source vidéo ou média.");

            var validationError = ValidateStreamingRequest(request);
            if (validationError.Length > 0) return StudioRuntimeResult.Failure(validationError);

            try
            {
                ConfigureStreamingMedia(request);
                using (var sceneSource = scene.Source)
                    Obs.SetOutputSource(0, sceneSource);

                var resources = CreateStreamingOutput(request);
                _streamingOutput = resources.Output;
                _streamingVideoEncoder = resources.VideoEncoder;
                _streamingAudioEncoder = resources.AudioEncoder;
                _streamingService = resources.Service;
                _streamingSceneId = request.Scene.Id;
                _streamingStarted = NewOutputSignal();
                _streamingStopped = NewOutputSignal();
                _streamingOutput.StateChanged += OnStreamingOutputStateChanged;
                startedTask = _streamingStarted.Task;
                _streamingOutput.Start();
            }
            catch (Exception exception)
            {
                ReleaseStreamingResourcesCore();
                return StudioRuntimeResult.Failure($"Démarrage du live impossible : {exception.Message}");
            }
        }

        try
        {
            var state = await startedTask.WaitAsync(cancellationToken);
            return state.State == ObsOutputState.Started
                ? StudioRuntimeResult.Success()
                : StudioRuntimeResult.Failure(StreamingStopMessage(state));
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                try
                {
                    _streamingOutput?.ForceStop();
                }
                catch
                {
                }
                ReleaseStreamingResourcesCore();
            }
            throw;
        }
    }

    public StudioRuntimeResult SwitchStreamingScene(Guid sceneId)
    {
        lock (_gate)
        {
            if (!IsAvailable) return StudioRuntimeResult.Unavailable(UnavailableMessageForOperation());
            if (_streamingOutput == null) return StudioRuntimeResult.Success();
            if (_streamingSceneId == sceneId) return StudioRuntimeResult.Success();
            if (!_scenes.TryGetValue(sceneId, out var scene))
                return StudioRuntimeResult.Failure("Cette scène n'existe pas dans LibObs.");

            try
            {
                using (var sceneSource = scene.Source)
                    Obs.SetOutputSource(0, sceneSource);
                _streamingSceneId = sceneId;
                return StudioRuntimeResult.Success();
            }
            catch (Exception exception)
            {
                return StudioRuntimeResult.Failure($"Changement de scène du live impossible : {exception.Message}");
            }
        }
    }

    public async Task<StudioRuntimeResult> StopStreamingAsync(CancellationToken cancellationToken)
    {
        Task<ObsOutputStateChangedEventArgs> stoppedTask;
        ObsOutput output;
        lock (_gate)
        {
            if (_streamingOutput == null || _streamingStopped == null)
                return StudioRuntimeResult.Success();

            output = _streamingOutput;
            stoppedTask = _streamingStopped.Task;
            try
            {
                _streamingStopRequested = true;
                output.Stop();
            }
            catch (Exception exception)
            {
                _streamingStopRequested = false;
                return StudioRuntimeResult.Failure($"Arrêt du live impossible : {exception.Message}");
            }
        }

        ObsOutputStateChangedEventArgs state;
        try
        {
            state = await stoppedTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                try
                {
                    if (ReferenceEquals(output, _streamingOutput)) output.ForceStop();
                }
                catch
                {
                }
                if (ReferenceEquals(output, _streamingOutput)) ReleaseStreamingResourcesCore();
            }
            throw;
        }

        lock (_gate)
        {
            if (ReferenceEquals(output, _streamingOutput)) ReleaseStreamingResourcesCore();
        }
        return state.StopCode is null or ObsOutputStopCode.Success
            ? StudioRuntimeResult.Success()
            : StudioRuntimeResult.Failure(StreamingStopMessage(state));
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_settingsService != null)
                _settingsService.SettingsSaved -= OnSettingsSaved;

            if (_recordingOutput != null)
            {
                try
                {
                    _recordingOutput.ForceStop();
                }
                catch
                {
                }
            }
            if (_streamingOutput != null)
            {
                try
                {
                    _streamingOutput.ForceStop();
                }
                catch
                {
                }
            }

            ReleaseRecordingResourcesCore();
            ReleaseStreamingResourcesCore();
            DisposeAllPreviewSessions();

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

    private static TaskCompletionSource<ObsOutputStateChangedEventArgs> NewOutputSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static string ValidateRecordingRequest(RecordingRequest request)
    {
        if (request.SceneId == Guid.Empty) return "L'identifiant de la scène est obligatoire.";
        if (string.IsNullOrWhiteSpace(request.OutputPath) || !Path.IsPathFullyQualified(request.OutputPath))
            return "Le chemin du fichier de sortie doit être absolu.";
        if (request.Fps <= 0 || request.VideoBitrateKbps <= 0 || request.AudioBitrateKbps <= 0)
            return "Les débits et le nombre d'images par seconde doivent être supérieurs à zéro.";
        if (request.AudioSampleRate <= 0 || request.AudioChannels is < 1 or > 2)
            return "La configuration audio doit être mono ou stéréo avec une fréquence valide.";
        if (request.BaseWidth <= 0 || request.BaseHeight <= 0 || request.OutputWidth <= 0 || request.OutputHeight <= 0)
            return "Les résolutions vidéo doivent être supérieures à zéro.";
        return "";
    }

    private void EnsureVideoSettingsForRecording(RecordingRequest request)
    {
        var desired = CreateRecordingVideoSettings(request);

        if (AreSameVideoSettings(_videoSettings, desired)) return;

        DisposeAllPreviewSessions();
        Obs.ResetVideo(desired);
        _videoSettings = desired;
        PreviewResetRequested?.Invoke(this, EventArgs.Empty);
    }

    internal static ObsVideoSettings CreateRecordingVideoSettings(RecordingRequest request) =>
        new()
        {
            FpsNumerator = (uint)request.Fps,
            BaseWidth = (uint)request.BaseWidth,
            BaseHeight = (uint)request.BaseHeight,
            OutputWidth = (uint)request.OutputWidth,
            OutputHeight = (uint)request.OutputHeight,
            OutputFormat = ObsVideoFormat.Nv12,
            ColorSpace = ObsVideoColorSpace.Rec709,
            Range = ObsVideoRange.Partial,
            ScaleType = ObsScaleType.Bicubic
        };

    private static void ConfigureRecordingMedia(RecordingRequest request)
    {
        Obs.ResetAudio(new ObsAudioSettings
        {
            SamplesPerSecond = (uint)request.AudioSampleRate,
            Speakers = request.AudioChannels == 1 ? ObsSpeakerLayout.Mono : ObsSpeakerLayout.Stereo
        });
    }

    private static RecordingResources CreateMuxerOutput(RecordingRequest request)
    {
        ObsEncoder? videoEncoder = null;
        ObsEncoder? audioEncoder = null;
        ObsOutput? output = null;
        try
        {
            using var videoSettings = new ObsData();
            videoSettings.SetString(ObsKnownSettings.Encoder.RateControl, "CBR");
            videoSettings.SetInt(ObsKnownSettings.Encoder.Bitrate, request.VideoBitrateKbps);
            videoSettings.SetInt(ObsKnownSettings.Encoder.KeyframeIntervalSeconds, 2);
            videoSettings.SetString(ObsKnownSettings.Encoder.Preset, "veryfast");
            videoEncoder = ObsEncoder.CreateVideo(ObsKnownIds.Encoders.X264, "castor-record-video", videoSettings);
            videoEncoder.AttachToVideo();

            using var audioSettings = new ObsData();
            audioSettings.SetInt(ObsKnownSettings.Encoder.Bitrate, request.AudioBitrateKbps);
            audioEncoder = ObsEncoder.CreateAudio(ObsKnownIds.Encoders.FfmpegAac, "castor-record-audio", settings: audioSettings);
            audioEncoder.AttachToAudio();

            using var outputSettings = new ObsData();
            outputSettings.SetString(ObsKnownSettings.Output.Path, request.OutputPath);
            outputSettings.SetString(ObsKnownSettings.Output.MuxerSettings, "");
            output = ObsOutput.Create(ObsKnownIds.Outputs.FfmpegMuxer, "castor-record-output", outputSettings);
            output.SetVideoEncoder(videoEncoder);
            output.SetAudioEncoder(audioEncoder);
            return new(output, videoEncoder, audioEncoder);
        }
        catch
        {
            output?.Dispose();
            audioEncoder?.Dispose();
            videoEncoder?.Dispose();
            throw;
        }
    }

    private static RecordingResources CreateWebMOutput(RecordingRequest request)
    {
        using var settings = new ObsData();
        settings.SetString("url", request.OutputPath);
        settings.SetString("format_name", "webm");
        settings.SetString("format_mime_type", "video/webm");
        settings.SetString(ObsKnownSettings.Output.MuxerSettings, "");
        settings.SetInt("video_bitrate", request.VideoBitrateKbps);
        settings.SetInt("audio_bitrate", request.AudioBitrateKbps);
        settings.SetInt("gop_size", request.Fps * 2);
        settings.SetString("video_encoder", LibVpxVp9EncoderName);
        settings.SetString("audio_encoder", LibOpusEncoderName);
        settings.SetInt("scale_width", request.OutputWidth);
        settings.SetInt("scale_height", request.OutputHeight);
        ObsOutput? output = null;
        try
        {
            output = ObsOutput.Create(FfmpegOutputId, "castor-record-output", settings);
            LibObsOutputInterop.SetAudioMixers(output, 1);
            return new(output, null, null);
        }
        catch
        {
            output?.Dispose();
            throw;
        }
    }

    internal static string ValidateStreamingRequest(StreamingRequest request)
    {
        if (request.Scene.Id == Guid.Empty) return "L'identifiant de la scène est obligatoire.";
        if (string.IsNullOrWhiteSpace(request.StreamKey)) return "La clé de stream Twitch est obligatoire.";
        if (request.Fps <= 0 || request.VideoBitrateKbps <= 0 || request.AudioBitrateKbps <= 0)
            return "Les débits et le nombre d'images par seconde doivent être supérieurs à zéro.";
        if (request.AudioSampleRate <= 0 || request.AudioChannels is < 1 or > 2)
            return "La configuration audio doit être mono ou stéréo avec une fréquence valide.";
        return "";
    }

    private static void ConfigureStreamingMedia(StreamingRequest request)
    {
        Obs.ResetAudio(new ObsAudioSettings
        {
            SamplesPerSecond = (uint)request.AudioSampleRate,
            Speakers = request.AudioChannels == 1 ? ObsSpeakerLayout.Mono : ObsSpeakerLayout.Stereo
        });
    }

    private static StreamingResources CreateStreamingOutput(StreamingRequest request)
    {
        ObsService? service = null;
        ObsEncoder? videoEncoder = null;
        ObsEncoder? audioEncoder = null;
        ObsOutput? output = null;
        try
        {
            using var serviceSettings = new ObsData();
            serviceSettings.SetString(ObsKnownSettings.Service.ServiceName, "Twitch");
            serviceSettings.SetString(ObsKnownSettings.Service.Server, "auto");
            serviceSettings.SetString(ObsKnownSettings.Service.StreamKey, request.StreamKey);
            service = ObsService.Create(
                ObsKnownIds.Services.CommonRtmp,
                "castor-twitch-service",
                serviceSettings);

            using var videoSettings = new ObsData();
            videoSettings.SetString(ObsKnownSettings.Encoder.RateControl, "CBR");
            videoSettings.SetInt(ObsKnownSettings.Encoder.Bitrate, request.VideoBitrateKbps);
            videoSettings.SetInt(ObsKnownSettings.Encoder.KeyframeIntervalSeconds, 2);
            videoSettings.SetString(ObsKnownSettings.Encoder.Preset, "veryfast");

            using var audioSettings = new ObsData();
            audioSettings.SetInt(ObsKnownSettings.Encoder.Bitrate, request.AudioBitrateKbps);
            service.ApplyEncoderSettings(videoSettings, audioSettings);

            videoEncoder = ObsEncoder.CreateVideo(
                ObsKnownIds.Encoders.X264,
                "castor-stream-video",
                videoSettings);
            videoEncoder.AttachToVideo();
            audioEncoder = ObsEncoder.CreateAudio(
                ObsKnownIds.Encoders.FfmpegAac,
                "castor-stream-audio",
                settings: audioSettings);
            audioEncoder.AttachToAudio();

            using var outputSettings = new ObsData();
            output = ObsOutput.Create(
                ObsKnownIds.Outputs.Rtmp,
                "castor-stream-output",
                outputSettings);
            output.SetService(service);
            output.SetVideoEncoder(videoEncoder);
            output.SetAudioEncoder(audioEncoder);
            return new(output, videoEncoder, audioEncoder, service);
        }
        catch
        {
            output?.Dispose();
            audioEncoder?.Dispose();
            videoEncoder?.Dispose();
            service?.Dispose();
            throw;
        }
    }

    private void OnRecordingOutputStateChanged(object? sender, ObsOutputStateChangedEventArgs args)
    {
        TaskCompletionSource<ObsOutputStateChangedEventArgs>? started = null;
        TaskCompletionSource<ObsOutputStateChangedEventArgs>? stopped = null;
        RecordingStateChangedEventArgs? notification = null;
        ObsOutput? unexpectedlyStoppedOutput = null;

        lock (_gate)
        {
            if (!ReferenceEquals(sender, _recordingOutput)) return;

            if (args.State == ObsOutputState.Started)
            {
                started = _recordingStarted;
                notification = new RecordingStateChangedEventArgs(true);
            }
            else if (args.State == ObsOutputState.Stopped)
            {
                started = _recordingStarted;
                stopped = _recordingStopped;
                notification = new RecordingStateChangedEventArgs(false, RecordingStopMessage(args));
                _recordingSceneId = null;
                if (!_recordingStopRequested) unexpectedlyStoppedOutput = _recordingOutput;
            }
        }

        started?.TrySetResult(args);
        stopped?.TrySetResult(args);
        if (notification != null) StateChanged?.Invoke(this, notification);
        if (unexpectedlyStoppedOutput != null)
            _ = ReleaseUnexpectedlyStoppedOutputAsync(unexpectedlyStoppedOutput);
    }

    private async Task ReleaseUnexpectedlyStoppedOutputAsync(ObsOutput output)
    {
        // ffmpeg_output emits its stop signal before its plugin stop callback has
        // necessarily finished writing the trailer.
        await Task.Delay(100);
        lock (_gate)
        {
            if (ReferenceEquals(output, _recordingOutput)) ReleaseRecordingResourcesCore();
        }
    }

    private void ReleaseRecordingResourcesCore()
    {
        var output = _recordingOutput;
        var videoEncoder = _recordingVideoEncoder;
        var audioEncoder = _recordingAudioEncoder;

        _recordingOutput = null;
        _recordingVideoEncoder = null;
        _recordingAudioEncoder = null;
        _recordingSceneId = null;
        _recordingStarted = null;
        _recordingStopped = null;
        _recordingStopRequested = false;

        if (output != null) output.StateChanged -= OnRecordingOutputStateChanged;
        try
        {
            if (_initialized) Obs.SetOutputSource(0, null);
        }
        catch
        {
        }
        try
        {
            output?.Dispose();
        }
        catch
        {
        }
        try
        {
            audioEncoder?.Dispose();
        }
        catch
        {
        }
        try
        {
            videoEncoder?.Dispose();
        }
        catch
        {
        }

        if (!_disposed && _streamingOutput == null && _pendingVideoSettings != null)
            ApplyVideoSettingsCore(_pendingVideoSettings);
    }

    private void OnStreamingOutputStateChanged(object? sender, ObsOutputStateChangedEventArgs args)
    {
        TaskCompletionSource<ObsOutputStateChangedEventArgs>? started = null;
        TaskCompletionSource<ObsOutputStateChangedEventArgs>? stopped = null;
        StreamingStateChangedEventArgs? notification = null;
        ObsOutput? unexpectedlyStoppedOutput = null;

        lock (_gate)
        {
            if (!ReferenceEquals(sender, _streamingOutput)) return;

            if (args.State == ObsOutputState.Started)
            {
                started = _streamingStarted;
                notification = new StreamingStateChangedEventArgs(true);
            }
            else if (args.State == ObsOutputState.Stopped)
            {
                started = _streamingStarted;
                stopped = _streamingStopped;
                notification = new StreamingStateChangedEventArgs(false, StreamingStopMessage(args));
                _streamingSceneId = null;
                if (!_streamingStopRequested) unexpectedlyStoppedOutput = _streamingOutput;
            }
        }

        started?.TrySetResult(args);
        stopped?.TrySetResult(args);
        if (notification != null) StreamingStateChanged?.Invoke(this, notification);
        if (unexpectedlyStoppedOutput != null)
            _ = ReleaseUnexpectedlyStoppedStreamingOutputAsync(unexpectedlyStoppedOutput);
    }

    private async Task ReleaseUnexpectedlyStoppedStreamingOutputAsync(ObsOutput output)
    {
        await Task.Delay(100);
        lock (_gate)
        {
            if (ReferenceEquals(output, _streamingOutput)) ReleaseStreamingResourcesCore();
        }
    }

    private void ReleaseStreamingResourcesCore()
    {
        var output = _streamingOutput;
        var videoEncoder = _streamingVideoEncoder;
        var audioEncoder = _streamingAudioEncoder;
        var service = _streamingService;

        _streamingOutput = null;
        _streamingVideoEncoder = null;
        _streamingAudioEncoder = null;
        _streamingService = null;
        _streamingSceneId = null;
        _streamingStarted = null;
        _streamingStopped = null;
        _streamingStopRequested = false;

        if (output != null) output.StateChanged -= OnStreamingOutputStateChanged;
        try
        {
            if (_initialized) Obs.SetOutputSource(0, null);
        }
        catch
        {
        }
        try
        {
            output?.Dispose();
        }
        catch
        {
        }
        try
        {
            audioEncoder?.Dispose();
        }
        catch
        {
        }
        try
        {
            videoEncoder?.Dispose();
        }
        catch
        {
        }
        try
        {
            service?.Dispose();
        }
        catch
        {
        }

        if (!_disposed && _recordingOutput == null && _pendingVideoSettings != null)
            ApplyVideoSettingsCore(_pendingVideoSettings);
    }

    private static string RecordingStopMessage(ObsOutputStateChangedEventArgs state)
    {
        if (!string.IsNullOrWhiteSpace(state.Error)) return state.Error;
        return state.StopCode switch
        {
            null or ObsOutputStopCode.Success => "",
            ObsOutputStopCode.BadPath => "Le chemin du fichier de sortie est invalide.",
            ObsOutputStopCode.NoSpace => "Espace disque insuffisant pour poursuivre l'enregistrement.",
            ObsOutputStopCode.EncodeError => "L'encodeur vidéo ou audio a rencontré une erreur.",
            ObsOutputStopCode.Unsupported => "Le format d'enregistrement n'est pas pris en charge.",
            _ => $"L'enregistrement s'est arrêté avec le code {state.StopCode}."
        };
    }

    private static string StreamingStopMessage(ObsOutputStateChangedEventArgs state)
    {
        if (!string.IsNullOrWhiteSpace(state.Error)) return state.Error;
        return state.StopCode switch
        {
            null or ObsOutputStopCode.Success => "",
            ObsOutputStopCode.ConnectFailed => "Connexion au serveur Twitch impossible.",
            ObsOutputStopCode.InvalidStream => "Twitch a refusé la clé de stream.",
            ObsOutputStopCode.Disconnected => "La connexion au serveur Twitch a été interrompue.",
            ObsOutputStopCode.EncodeError => "L'encodeur vidéo ou audio a rencontré une erreur.",
            ObsOutputStopCode.Unsupported => "La configuration du live n'est pas prise en charge.",
            _ => $"Le live s'est arrêté avec le code {state.StopCode}."
        };
    }

    private sealed record RecordingResources(
        ObsOutput Output,
        ObsEncoder? VideoEncoder,
        ObsEncoder? AudioEncoder);

    private sealed record StreamingResources(
        ObsOutput Output,
        ObsEncoder VideoEncoder,
        ObsEncoder AudioEncoder,
        ObsService Service);

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

    internal static bool HasWinRtCaptureRuntime(string baseDirectory) =>
        File.Exists(Path.Combine(baseDirectory, WinRtRuntimeRelativePath));

    internal static ObsWindowsDisplayCaptureSettings CreateDisplayCaptureSettings(
        string monitorId,
        string baseDirectory) => new()
        {
            MonitorId = monitorId,
            Method = HasWinRtCaptureRuntime(baseDirectory)
                ? ObsWindowsDisplayCaptureMethod.Automatic
                : ObsWindowsDisplayCaptureMethod.DxgiDesktopDuplication
        };

    internal static ObsWindowsWindowCaptureSettings CreateWindowCaptureSettings(
        string window,
        string baseDirectory) => new()
        {
            Window = window,
            Method = HasWinRtCaptureRuntime(baseDirectory)
                ? ObsWindowsWindowCaptureMethod.Automatic
                : ObsWindowsWindowCaptureMethod.BitBlt
        };

    private static ObsSource CreateNativeSource(SourceAddRequest request) => request switch
    {
        SourceAddRequest.Video video => video.Option.Type switch
        {
            VideoCaptureKind.Monitor => ObsSource.CreateWindowsDisplayCapture(video.RequestedName,
                CreateDisplayCaptureSettings(video.Option.Id, AppContext.BaseDirectory)),
            VideoCaptureKind.Window => ObsSource.CreateWindowsWindowCapture(video.RequestedName,
                CreateWindowCaptureSettings(video.Option.Id, AppContext.BaseDirectory)),
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

    // Tears down every live preview session - used wherever Obs.ResetVideo() runs, since
    // that invalidates the shared canvas every session renders against.
    private void DisposeAllPreviewSessions()
    {
        if (_previewSessions.Count == 0) return;

        var sessions = _previewSessions.Values.ToArray();
        _previewSessions.Clear();
        foreach (var session in sessions)
            ReleasePreviewSession(session);
    }

    // Tears down every session currently showing a given scene - used when that scene is
    // removed, since none of them have anything left to render.
    private void DisposePreviewSessionsForScene(Guid sceneId)
    {
        if (_previewSessions.Count == 0) return;

        List<IntPtr>? handles = null;
        foreach (var (handle, session) in _previewSessions)
        {
            if (session.SceneId != sceneId) continue;
            handles ??= [];
            handles.Add(handle);
        }

        if (handles == null) return;
        foreach (var handle in handles)
            DisposePreviewSession(handle);
    }

    private void DisposePreviewSession(IntPtr windowHandle)
    {
        if (!_previewSessions.Remove(windowHandle, out var session)) return;
        ReleasePreviewSession(session);
    }

    private static void ReleasePreviewSession(PreviewSession session)
    {
        try
        {
            session.Display.Dispose();
        }
        catch
        {
        }

        try
        {
            session.View.Dispose();
        }
        catch
        {
        }

        try
        {
            session.SceneSource.Dispose();
        }
        catch
        {
        }
    }

    private static void RenderPreviewFrame(ObsDisplayFrame frame, PreviewSession session) =>
        ObsPreviewGraphics.RenderScene(
            frame,
            session.SceneSource,
            session.CanvasWidth,
            session.CanvasHeight);

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
