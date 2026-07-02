using System.Diagnostics;
using Castor.Engine.Models;
using Castor.Native;

namespace Castor.Engine.Services;

public sealed class RecorderService(IMediaMtxService mediaMtxService) : IRecorderService
{
    private IntPtr _recorderPtr = IntPtr.Zero;
    private IntPtr _streamPtr = IntPtr.Zero;

    private readonly object _nativeRecorderLock = new();
    private readonly Dictionary<Guid, IntPtr> _previewPtrs = new();
    private readonly object _previewLock = new();
    private readonly SemaphoreSlim _previewSemaphore = new(1, 1);
    private readonly SemaphoreSlim _switchSemaphore = new(1, 1);

    public bool IsRecording
    {
        get
        {
            lock (_nativeRecorderLock)
                return _recorderPtr != IntPtr.Zero;
        }
    }

    public bool IsStreaming
    {
        get
        {
            lock (_nativeRecorderLock)
                return _streamPtr != IntPtr.Zero;
        }
    }

    public event Action? RecordingStarted;
    public event Action? RecordingStopped;
    public event Action? StreamingStarted;
    public event Action? StreamingStopped;

    public bool IsPreviewActive(Guid sceneId)
    {
        lock (_previewLock)
            return _previewPtrs.TryGetValue(sceneId, out var pointer) && pointer != IntPtr.Zero;
    }

    public int Start(SceneItem scene, string outputPath,
                     int fps = 30,
                     int videoBitrateKbps = 0,
                     CastorVideoCodec videoCodec = CastorVideoCodec.H264,
                     CastorAudioCodec audioCodec = CastorAudioCodec.AAC,
                     int outputWidth = 0, int outputHeight = 0,
                     int qualityIndex = 1)
    {
        lock (_nativeRecorderLock)
        {
            if (_recorderPtr != IntPtr.Zero) return -1;
        }

        var videoItem = GetVideoSource(scene);
        var audioItem = GetAudioSource(scene);

        if (videoItem == null) return -2;

        var videoSrc = GetVideoSourceInfo(videoItem);
        if (videoSrc == null) return -2;

        var config = new RecorderConfig
        {
            Streams = new StreamConfig[8],
            NumStreams = 1,
            Fps = fps
        };

        config.Streams[0] = new StreamConfig
        {
            VideoSrc = videoSrc.Value,
            AudioSrc = GetAudioSourceInfo(audioItem) ?? default,
            Output = new OutputConfig
            {
                Type             = CastorOutputType.File,
                Destination      = outputPath,
                VideoBitrateKbps = videoBitrateKbps,
                VideoCodec       = videoCodec,
                AudioCodec       = audioCodec,
                OutputWidth      = outputWidth,
                OutputHeight     = outputHeight,
                QualityIndex     = qualityIndex,
            }
        };

        lock (_nativeRecorderLock)
        {
            if (_recorderPtr != IntPtr.Zero) return -1;

            _recorderPtr = CastorNative.RecorderCreate(ref config);
            if (_recorderPtr == IntPtr.Zero) return -3;

            int result = CastorNative.RecorderStart(_recorderPtr);
            if (result != 0)
            {
                CastorNative.RecorderDestroy(_recorderPtr);
                _recorderPtr = IntPtr.Zero;
                return result;
            }
        }

        RecordingStarted?.Invoke();
        return 0;
    }

    public void StopRecording()
    {
        lock (_nativeRecorderLock)
        {
            if (_recorderPtr == IntPtr.Zero) return;
            CastorNative.RecorderStop(_recorderPtr);
            CastorNative.RecorderDestroy(_recorderPtr);
            _recorderPtr = IntPtr.Zero;
        }

        RecordingStopped?.Invoke();
    }

    public void StopAll()
    {
        StopRecording();
        StopStream();
        StopAllPreviews();
    }

    public int StartStream(SceneItem scene, StreamingPlatform platform, string streamKeyOrUrl,
                           int fps = 30, int videoBitrateKbps = 4000)
    {
        lock (_nativeRecorderLock)
        {
            if (_streamPtr != IntPtr.Zero) return -1;
        }

        var videoItem = GetVideoSource(scene);
        var audioItem = GetAudioSource(scene);

        if (videoItem == null) return -2;

        var videoSrc = GetVideoSourceInfo(videoItem);
        if (videoSrc == null) return -2;

        string? rtmpUrl = CastorNative.GetStreamingUrl(ToNativePlatform(platform), streamKeyOrUrl);
        Debug.WriteLine($"[Stream] service={platform} key='{streamKeyOrUrl}' -> url='{rtmpUrl}'");
        if (string.IsNullOrEmpty(rtmpUrl)) return -4;

        var audioSrc = GetAudioSourceInfo(audioItem) ?? default;

        Debug.WriteLine($"[Stream] RecorderCreate -> scene='{scene.Name}', src.Type={videoSrc.Value.Type}, audioItem={(audioItem != null ? audioSrc.Label : "none")}");

        var config = new RecorderConfig
        {
            Streams = new StreamConfig[8],
            NumStreams = 1,
            Fps = fps
        };

        config.Streams[0] = new StreamConfig
        {
            VideoSrc = videoSrc.Value,
            AudioSrc = audioSrc,
            Output = new OutputConfig
            {
                Type             = CastorOutputType.Rtmp,
                Destination      = rtmpUrl,
                VideoBitrateKbps = videoBitrateKbps,
                AudioBitrateKbps = 128,
                GopSeconds       = 2
            }
        };

        lock (_nativeRecorderLock)
        {
            if (_streamPtr != IntPtr.Zero) return -1;

            _streamPtr = CastorNative.RecorderCreate(ref config);
            if (_streamPtr == IntPtr.Zero) return -3;

            int result = CastorNative.RecorderStart(_streamPtr);
            if (result != 0)
            {
                CastorNative.RecorderDestroy(_streamPtr);
                _streamPtr = IntPtr.Zero;
                return result;
            }
        }

        StreamingStarted?.Invoke();
        return 0;
    }

    public void StopStream()
    {
        lock (_nativeRecorderLock)
        {
            if (_streamPtr == IntPtr.Zero) return;
            CastorNative.RecorderStop(_streamPtr);
            CastorNative.RecorderDestroy(_streamPtr);
            _streamPtr = IntPtr.Zero;
        }

        StreamingStopped?.Invoke();
    }

    public int StartPreview(SceneItem scene, int fps = 30)
    {
        _previewSemaphore.Wait();
        try
        {
            lock (_previewLock)
            {
                if (_previewPtrs.TryGetValue(scene.Id, out var existing) && existing != IntPtr.Zero)
                {
                    Debug.WriteLine($"[Preview] '{scene.Name}' déjà actif — ignoré.");
                    return 0;
                }
            }

            var videoItem = GetVideoSource(scene);
            var audioItem = GetAudioSource(scene);

            if (videoItem == null) return -2;

            var videoSrc = GetVideoSourceInfo(videoItem);
            if (videoSrc == null) return -2;

            var config = new RecorderConfig
            {
                Streams = new StreamConfig[8],
                NumStreams = 1,
                Fps = fps
            };

            config.Streams[0] = new StreamConfig
            {
                VideoSrc = videoSrc.Value,
                AudioSrc = GetAudioSourceInfo(audioItem) ?? default,
                Output = new OutputConfig
                {
                    Type = CastorOutputType.Rtmp,
                    Destination = mediaMtxService.GetPreviewPushUrl(scene.Id),
                    VideoBitrateKbps = 3000,
                    AudioBitrateKbps = 128,
                    GopSeconds = 0,
                }
            };

            Debug.WriteLine($"[Preview] RecorderCreate -> scène='{scene.Name}', url={mediaMtxService.GetPreviewPushUrl(scene.Id)}");

            IntPtr pointer;
            lock (_nativeRecorderLock)
            {
                pointer = CastorNative.RecorderCreate(ref config);
                if (pointer == IntPtr.Zero) return -3;

                int result = CastorNative.RecorderStart(pointer);
                if (result != 0)
                {
                    CastorNative.RecorderDestroy(pointer);
                    return result;
                }
            }

            lock (_previewLock)
                _previewPtrs[scene.Id] = pointer;

            Debug.WriteLine($"[Preview] '{scene.Name}' démarré -> {mediaMtxService.GetPreviewPushUrl(scene.Id)}");
            return 0;
        }
        finally
        {
            _previewSemaphore.Release();
        }
    }

    public void StopPreview(SceneItem scene)
    {
        _previewSemaphore.Wait();
        try
        {
            StopPreviewUnsafe(scene.Id);
        }
        finally
        {
            _previewSemaphore.Release();
        }
    }

    public void StopAllPreviews()
    {
        _previewSemaphore.Wait();
        try
        {
            Dictionary<Guid, IntPtr> copy;
            lock (_previewLock)
            {
                copy = new Dictionary<Guid, IntPtr>(_previewPtrs);
                _previewPtrs.Clear();
            }

            foreach (var (_, pointer) in copy)
            {
                if (pointer == IntPtr.Zero) continue;
                lock (_nativeRecorderLock)
                {
                    CastorNative.RecorderStop(pointer);
                    CastorNative.RecorderDestroy(pointer);
                }
            }
        }
        finally
        {
            _previewSemaphore.Release();
        }
    }

    public int SwitchScene(SceneItem scene)
    {
        _switchSemaphore.Wait();
        try
        {
            var videoItem = GetVideoSource(scene);
            if (videoItem == null) return -1;

            var videoSrc = GetVideoSourceInfo(videoItem);
            if (videoSrc == null) return -1;

            int result = 0;

            lock (_nativeRecorderLock)
            {
                if (_recorderPtr != IntPtr.Zero)
                    result = CastorNative.RecorderSwitchVideoSource(_recorderPtr, streamIndex: 0, videoSrc.Value);

                if (_streamPtr != IntPtr.Zero && result == 0)
                    result = CastorNative.RecorderSwitchVideoSource(_streamPtr, streamIndex: 0, videoSrc.Value);
            }

            return result;
        }
        finally
        {
            _switchSemaphore.Release();
        }
    }

    private void StopPreviewUnsafe(Guid sceneId)
    {
        IntPtr pointer;
        lock (_previewLock)
        {
            if (!_previewPtrs.TryGetValue(sceneId, out pointer) || pointer == IntPtr.Zero) return;
            _previewPtrs.Remove(sceneId);
        }

        lock (_nativeRecorderLock)
        {
            CastorNative.RecorderStop(pointer);
            CastorNative.RecorderDestroy(pointer);
        }
    }

    private static SourceItem? GetVideoSource(SceneItem scene)
    {
        return scene.Sources.FirstOrDefault(source => source.Kind == SourceKind.Video);
    }

    private static SourceItem? GetAudioSource(SceneItem scene)
    {
        return scene.Sources.FirstOrDefault(source => source.Kind == SourceKind.Audio);
    }

    private static CaptureSourceInfo? GetVideoSourceInfo(SourceItem? source)
    {
        if (source == null) return null;

        if (source.NativeDescriptor is CaptureSourceInfo info)
            return info;

        // Fichier vidéo : chemin dans SymbolicLink, loop lu depuis SourceItem.Loop
        if (source.NativeDescriptor is FileSourceInfo fileInfo)
            return new CaptureSourceInfo
            {
                Label        = source.Name,
                Type         = CaptureSourceType.File,
                SymbolicLink = fileInfo.FilePath,
                Index        = source.Loop ? 1 : 0,
            };

        return null;
    }

    private static AudioSourceInfo? GetAudioSourceInfo(SourceItem? source)
    {
        if (source == null) return null;

        if (source.NativeDescriptor is AudioSourceInfo info)
            return info;

        // Fichier audio : chemin dans DeviceId, loop lu depuis SourceItem.Loop
        if (source.NativeDescriptor is FileSourceInfo fileInfo)
            return new AudioSourceInfo
            {
                Label    = source.Name,
                Type     = AudioSourceType.File,
                DeviceId = fileInfo.FilePath,
                Index    = source.Loop ? 1 : 0,
            };

        return null;
    }

    private static CastorServiceType ToNativePlatform(StreamingPlatform platform)
    {
        return platform switch
        {
            StreamingPlatform.Twitch => CastorServiceType.Twitch,
            StreamingPlatform.YouTube => CastorServiceType.YouTube,
            _ => CastorServiceType.Custom
        };
    }
}
