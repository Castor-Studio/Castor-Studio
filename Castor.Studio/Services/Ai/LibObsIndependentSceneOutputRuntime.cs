using CastorApplication.Models.Studio;
using CastorApplication.Services.Studio;
using LibObs;

namespace CastorApplication.Services.Ai;

/// <summary>
/// Creates one encoded RTMP output per scene using an independent libobs view: its own
/// video mix (<see cref="ObsView.Add"/>) feeds the encoder, which leaves the main program
/// output untouched.
/// </summary>
internal sealed class LibObsIndependentSceneOutputRuntime : IIndependentSceneOutputRuntime
{
    private sealed class AuxiliaryOutput
    {
        public required Guid SceneId { get; init; }
        public required ObsView View { get; init; }
        public required ObsOutput Output { get; init; }
        public required ObsEncoder VideoEncoder { get; init; }
        public required ObsEncoder AudioEncoder { get; init; }
        public required ObsService Service { get; init; }
        public required EventHandler<ObsOutputStateChangedEventArgs> StateHandler { get; init; }
    }

    private readonly IAiSceneSourceProvider _sceneSources;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, AuxiliaryOutput> _outputs = [];
    private bool _disposed;
    private string _unavailableMessage = "";

    public bool IsAvailable => !_disposed && _sceneSources.IsAvailable && string.IsNullOrEmpty(_unavailableMessage);
    public string UnavailableMessage => _unavailableMessage.Length > 0
        ? _unavailableMessage
        : "LibObs ne fournit pas de pipeline vidéo utilisable.";

    public event EventHandler<IndependentSceneOutputStateChangedEventArgs>? StateChanged;

    public LibObsIndependentSceneOutputRuntime(IAiSceneSourceProvider sceneSources)
    {
        _sceneSources = sceneSources;
        try
        {
            if (Obs.VideoInfo == null)
                _unavailableMessage = "La vidéo LibObs n'est pas active.";
        }
        catch (Exception exception)
        {
            _unavailableMessage = $"Le pipeline vidéo LibObs est indisponible : {exception.Message}";
        }
    }

    public Task StartAsync(SceneDefinition scene, string pushUrl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scene);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsAvailable) return Task.FromException(new InvalidOperationException(UnavailableMessage));
        if (scene.Id == Guid.Empty) return Task.FromException(new ArgumentException("L'identifiant de scène est obligatoire."));
        if (string.IsNullOrWhiteSpace(pushUrl)) return Task.FromException(new ArgumentException("L'URL RTMP est obligatoire."));

        lock (_gate)
        {
            if (_outputs.ContainsKey(scene.Id)) return Task.CompletedTask;

            AuxiliaryOutput? resources = null;
            ObsSource? source = null;
            ObsView? view = null;
            ObsService? service = null;
            ObsEncoder? videoEncoder = null;
            ObsEncoder? audioEncoder = null;
            ObsOutput? output = null;
            EventHandler<ObsOutputStateChangedEventArgs>? stateHandler = null;
            var registered = false;
            try
            {
                source = _sceneSources.AcquireSceneSource(scene.Id)
                    ?? throw new InvalidOperationException("La scène n'existe pas dans LibObs.");

                view = ObsView.Create();
                view.SetSource(0, source);
                var videoInfo = Obs.VideoInfo
                    ?? throw new InvalidOperationException("La vidéo LibObs n'est pas active.");
                // Un mix vidéo à part, rendu à chaque image comme la sortie principale ; le
                // détruire avec la vue le retire de la boucle de rendu.
                var video = view.Add(videoInfo);

                service = CreateService(scene.Id, pushUrl);
                videoEncoder = CreateVideoEncoder(scene.Id);
                audioEncoder = CreateAudioEncoder(scene.Id);
                videoEncoder.SetVideo(video);
                audioEncoder.SetAudio(Obs.GetAudio());

                using var outputSettings = new ObsData();
                output = ObsOutput.Create(
                    ObsKnownIds.Outputs.Rtmp,
                    $"castor-ai-output-{scene.Id:N}",
                    outputSettings);
                output.SetService(service);
                output.SetVideoEncoder(videoEncoder);
                output.SetAudioEncoder(audioEncoder, UIntPtr.Zero);

                stateHandler = (_, args) =>
                    OnOutputStateChanged(scene.Id, output, args);
                output.StateChanged += stateHandler;
                resources = new AuxiliaryOutput
                {
                    SceneId = scene.Id,
                    View = view,
                    Output = output,
                    VideoEncoder = videoEncoder,
                    AudioEncoder = audioEncoder,
                    Service = service,
                    StateHandler = stateHandler
                };

                _outputs.Add(scene.Id, resources);
                registered = true;
                // libobs performs the network handshake asynchronously. Start() lets
                // the output report reconnect/failure through StateChanged instead of
                // turning a transient mediaMTX connection race into a setup rollback.
                output.Start();

                StateChanged?.Invoke(this, new IndependentSceneOutputStateChangedEventArgs(
                    scene.Id, IndependentSceneOutputState.Started));
                resources = null;
                view = null;
                return Task.CompletedTask;
            }
            catch (EntryPointNotFoundException exception)
            {
                _unavailableMessage = $"La version de LibObs ne supporte pas les vues indépendantes : {exception.Message}";
                throw new InvalidOperationException(UnavailableMessage, exception);
            }
            finally
            {
                source?.Dispose();
                if (resources != null)
                {
                    var ownsResources = !registered || _outputs.Remove(scene.Id);
                    if (ownsResources) StopAndDispose(resources);
                }
                else if (view != null)
                {
                    try
                    {
                        if (output != null)
                        {
                            if (stateHandler != null) output.StateChanged -= stateHandler;
                            if (output.IsActive) output.ForceStop();
                            output.Dispose();
                        }
                    }
                    catch { }
                    try { audioEncoder?.Dispose(); } catch { }
                    try { videoEncoder?.Dispose(); } catch { }
                    try { service?.Dispose(); } catch { }
                    try { view.Dispose(); } catch { }
                }
            }
        }
    }

    public Task StopAsync(Guid sceneId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AuxiliaryOutput? resources;
        lock (_gate)
        {
            if (!_outputs.Remove(sceneId, out resources)) return Task.CompletedTask;
        }

        StopAndDispose(resources!);
        StateChanged?.Invoke(this, new IndependentSceneOutputStateChangedEventArgs(
            sceneId, IndependentSceneOutputState.Stopped));
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        AuxiliaryOutput[] resources;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            resources = _outputs.Values.ToArray();
            _outputs.Clear();
        }

        foreach (var output in resources) StopAndDispose(output);
    }

    private static ObsService CreateService(Guid sceneId, string pushUrl)
    {
        using var settings = new ObsData();
        settings.SetString(ObsKnownSettings.Service.ServiceName, "Custom");
        settings.SetString(ObsKnownSettings.Service.Server, pushUrl.Trim());
        settings.SetString(ObsKnownSettings.Service.StreamKey, "");
        return ObsService.Create(
            ObsKnownIds.Services.CustomRtmp,
            $"castor-ai-service-{sceneId:N}",
            settings);
    }

    private static ObsEncoder CreateVideoEncoder(Guid sceneId)
    {
        using var settings = new ObsData();
        settings.SetString(ObsKnownSettings.Encoder.RateControl, "CBR");
        settings.SetInt(ObsKnownSettings.Encoder.Bitrate, 2500);
        settings.SetInt(ObsKnownSettings.Encoder.KeyframeIntervalSeconds, 2);
        settings.SetString(ObsKnownSettings.Encoder.Preset, "veryfast");
        return ObsEncoder.CreateVideo(ObsKnownIds.Encoders.X264, $"castor-ai-video-{sceneId:N}", settings);
    }

    private static ObsEncoder CreateAudioEncoder(Guid sceneId)
    {
        using var settings = new ObsData();
        settings.SetInt(ObsKnownSettings.Encoder.Bitrate, 160);
        return ObsEncoder.CreateAudio(
            ObsKnownIds.Encoders.FfmpegAac,
            $"castor-ai-audio-{sceneId:N}",
            settings: settings);
    }

    private void OnOutputStateChanged(Guid sceneId, ObsOutput output, ObsOutputStateChangedEventArgs args)
    {
        var failed = args.State == ObsOutputState.Stopped &&
            (args.StopCode is { } stopCode && stopCode != ObsOutputStopCode.Success ||
             !string.IsNullOrWhiteSpace(args.Error));
        var state = failed
            ? IndependentSceneOutputState.Error
            : args.State switch
            {
                ObsOutputState.Reconnecting => IndependentSceneOutputState.Reconnecting,
                ObsOutputState.Stopped => IndependentSceneOutputState.Stopped,
                _ => IndependentSceneOutputState.Started
            };
        var message = args.Error ?? output.LastError ?? "";
        StateChanged?.Invoke(this, new IndependentSceneOutputStateChangedEventArgs(sceneId, state, message));

        if (args.State == ObsOutputState.Stopped)
        {
            AuxiliaryOutput? resources = null;
            lock (_gate)
            {
                if (_outputs.TryGetValue(sceneId, out var current) && ReferenceEquals(current.Output, output))
                {
                    _outputs.Remove(sceneId);
                    resources = current;
                }
            }
            if (resources != null) DisposeOutput(resources);
        }
    }

    private static void StopAndDispose(AuxiliaryOutput resources)
    {
        try
        {
            resources.Output.StateChanged -= resources.StateHandler;
            if (resources.Output.IsActive) resources.Output.ForceStop();
        }
        catch { }
        DisposeOutput(resources);
    }

    private static void DisposeOutput(AuxiliaryOutput? resources)
    {
        if (resources == null) return;
        try { resources.Output.StateChanged -= resources.StateHandler; } catch { }
        try { resources.Output.Dispose(); } catch { }
        try { resources.AudioEncoder.Dispose(); } catch { }
        try { resources.VideoEncoder.Dispose(); } catch { }
        try { resources.Service.Dispose(); } catch { }
        try { resources.View.Dispose(); } catch { }
    }
}
