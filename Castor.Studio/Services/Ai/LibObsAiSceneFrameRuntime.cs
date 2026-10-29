using System.Diagnostics;
using CastorApplication.Services.Studio;
using LibObs;

namespace CastorApplication.Services.Ai;

/// <summary>
/// One independent libobs view per scene (its own video mix, <see cref="ObsView.Add"/>), read by a
/// raw output that converts the frames to BGRA at the size the AI asks for. Nothing is encoded and
/// the program output is left untouched.
/// </summary>
internal sealed class LibObsAiSceneFrameRuntime : IAiSceneFrameRuntime
{
    private sealed record SceneRender(ObsView View, ObsRawOutput Output, int Width, int Height);

    private readonly IAiSceneSourceProvider _sceneSources;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, SceneRender> _renders = [];
    private volatile AiSceneFrameSink? _sink;
    private bool _disposed;

    public LibObsAiSceneFrameRuntime(IAiSceneSourceProvider sceneSources) => _sceneSources = sceneSources;

    public bool IsAvailable => !_disposed && _sceneSources.IsAvailable && VideoInfo() != null;

    public string UnavailableMessage => "Le rendu des scènes pour l'IA nécessite la vidéo LibObs active.";

    public void Start(IReadOnlyCollection<Guid> sceneIds, int width, int height, AiSceneFrameSink sink)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (!IsAvailable) throw new InvalidOperationException(UnavailableMessage);

        var failures = new List<string>();
        lock (_gate)
        {
            _sink = sink;
            foreach (var (sceneId, render) in _renders.ToList())
            {
                if (sceneIds.Contains(sceneId) && render.Width == width && render.Height == height) continue;
                _renders.Remove(sceneId);
                Release(render);
            }

            foreach (var sceneId in sceneIds)
            {
                if (_renders.ContainsKey(sceneId)) continue;
                try { _renders.Add(sceneId, Create(sceneId, width, height)); }
                catch (Exception exception) { failures.Add(exception.Message); }
            }
        }

        if (failures.Count > 0) throw new InvalidOperationException(string.Join(" ", failures.Distinct()));
    }

    public void StopAll()
    {
        SceneRender[] renders;
        lock (_gate)
        {
            _sink = null;
            renders = _renders.Values.ToArray();
            _renders.Clear();
        }

        foreach (var render in renders) Release(render);
    }

    public void Dispose()
    {
        if (_disposed) return;
        StopAll();
        _disposed = true;
    }

    private SceneRender Create(Guid sceneId, int width, int height)
    {
        using var source = _sceneSources.AcquireSceneSource(sceneId)
            ?? throw new InvalidOperationException("La scène n'existe pas dans LibObs.");
        var videoInfo = VideoInfo() ?? throw new InvalidOperationException(UnavailableMessage);

        var view = ObsView.Create();
        try
        {
            view.SetSource(0, source);
            // libobs scales the mix itself: frames arrive at the AI size, the SDK has nothing to resize.
            var video = view.Add(videoInfo with { OutputWidth = (uint)width, OutputHeight = (uint)height });
            var output = ObsRawOutput.Create($"castor-ai-frames-{sceneId:N}", new ObsRawOutputSettings
            {
                Video = video,
                VideoConversion = new ObsVideoConversion(
                    ObsVideoFormat.Bgra, (uint)width, (uint)height, ObsVideoRange.Full, ObsVideoColorSpace.Default),
                // Built once per scene: the video thread allocates nothing per frame.
                OnVideo = frame => OnFrame(sceneId, frame),
            });
            output.CallbackFailed += (_, args) => Debug.WriteLine($"[Castor.AI frames {sceneId:N}] {args}");
            try
            {
                output.Start();
            }
            catch
            {
                output.Dispose();
                throw;
            }

            return new SceneRender(view, output, width, height);
        }
        catch
        {
            view.Dispose();
            throw;
        }
    }

    // libobs video thread. The planes are only valid during the call: the SDK copies them.
    private void OnFrame(Guid sceneId, ObsRawVideoFrame frame) =>
        _sink?.Invoke(sceneId, frame.GetPlane(0), (int)frame.Width, (int)frame.Height, frame.GetLinesize(0));

    private static void Release(SceneRender render)
    {
        // The output first: it waits for running callbacks, then the mix can leave the render loop.
        try { render.Output.Dispose(); }
        catch (Exception exception) { Debug.WriteLine($"[Castor.AI frames] output release failed: {exception.Message}"); }
        try { render.View.Dispose(); }
        catch (Exception exception) { Debug.WriteLine($"[Castor.AI frames] view release failed: {exception.Message}"); }
    }

    private static ObsVideoInfo? VideoInfo()
    {
        try { return Obs.VideoInfo; }
        catch { return null; }
    }
}
