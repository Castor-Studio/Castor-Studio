using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Castor.Native;
using CastorApplication.Models;

namespace CastorApplication.Services;

/// <summary>
/// Gère le cycle de vie du recorder natif (create → start → stop → destroy).
/// </summary>
public sealed class RecorderService
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    private static RecorderService? _instance;
    public static RecorderService Instance => _instance ??= new RecorderService();

    // ── État ──────────────────────────────────────────────────────────────────
    private IntPtr _recorderPtr = IntPtr.Zero;
    private IntPtr _streamPtr   = IntPtr.Zero;

    /// <summary>Un preview par scène, indexé par SceneItem.Id.</summary>
    private readonly Dictionary<Guid, IntPtr> _previewPtrs = new();
    private readonly object _previewLock = new();

    /// <summary>
    /// Sérialise toutes les opérations de preview (Start/Stop) pour éviter les
    /// race conditions entre threads : un seul appel natif RecorderCreate/Stop/Destroy
    /// peut se produire à la fois.
    /// </summary>
    private readonly SemaphoreSlim _previewSemaphore = new(1, 1);

    /// <summary>
    /// Sérialise les appels à SwitchScene : si deux switches arrivent très vite,
    /// le second attend que le premier appel natif soit terminé pour éviter un
    /// appel concurrent à RecorderSwitchVideoSource sur le même pointeur.
    /// </summary>
    private readonly SemaphoreSlim _switchSemaphore = new(1, 1);

    public bool IsRecording => _recorderPtr != IntPtr.Zero;
    public bool IsStreaming => _streamPtr   != IntPtr.Zero;

    // ── Events d'état (abonnés par ScenesViewModel pour l'auto-mute) ──────────
    public event Action? RecordingStarted;
    public event Action? RecordingStopped;
    public event Action? StreamingStarted;
    public event Action? StreamingStopped;

    /// <summary>Retourne true si un flux de preview est actif pour cette scène.</summary>
    public bool IsPreviewActive(Guid sceneId)
    {
        lock (_previewLock)
            return _previewPtrs.TryGetValue(sceneId, out var p) && p != IntPtr.Zero;
    }

    private RecorderService() { }

    // ── API publique ──────────────────────────────────────────────────────────

    /// <summary>
    /// Démarre l'enregistrement de la scène vers outputPath.
    /// Retourne 0 si succès, code d'erreur négatif sinon.
    /// </summary>
    public int Start(SceneItem scene, string outputPath, int fps = 30,
                     int videoBitrateKbps = 0,
                     CastorVideoCodec videoCodec = CastorVideoCodec.H264,
                     CastorAudioCodec audioCodec = CastorAudioCodec.AAC,
                     int outputWidth = 0, int outputHeight = 0,
                     int qualityIndex = 1)
    {
        if (IsRecording) return -1;

        // ── Cherche la première source vidéo et la première source audio ──────
        var videoItem = scene.Sources.FirstOrDefault(s => s.Type == "Vidéo" && s.Tag is CaptureSourceInfo);
        var audioItem = scene.Sources.FirstOrDefault(s => s.Type == "Audio" && s.Tag is AudioSourceInfo);

        if (videoItem == null)
            return -2; // pas de source vidéo dans la scène

        var videoSrc = (CaptureSourceInfo)videoItem.Tag!;
        var audioSrc = audioItem != null ? (AudioSourceInfo)audioItem.Tag! : default;

        // ── Construit RecorderConfig ──────────────────────────────────────────
        var config = new RecorderConfig
        {
            Streams    = new StreamConfig[8], // zéro-initialisé
            NumStreams = 1,
            Fps        = fps
        };

        config.Streams[0] = new StreamConfig
        {
            VideoSrc = videoSrc,
            AudioSrc = audioSrc,
            Output   = new OutputConfig
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

        // ── Crée et démarre le recorder ───────────────────────────────────────
        _recorderPtr = CastorNative.RecorderCreate(ref config);
        if (_recorderPtr == IntPtr.Zero)
            return -3;

        int result = CastorNative.RecorderStart(_recorderPtr);
        if (result != 0)
        {
            CastorNative.RecorderDestroy(_recorderPtr);
            _recorderPtr = IntPtr.Zero;
            return result;
        }

        RecordingStarted?.Invoke();
        return 0;
    }

    /// <summary>
    /// Arrête uniquement l'enregistrement en cours.
    /// Les previews et le stream restent actifs.
    /// </summary>
    public void StopRecording()
    {
        if (_recorderPtr == IntPtr.Zero) return;
        CastorNative.RecorderStop(_recorderPtr);
        CastorNative.RecorderDestroy(_recorderPtr);
        _recorderPtr = IntPtr.Zero;
        RecordingStopped?.Invoke();
    }

    /// <summary>
    /// Arrête tout (enregistrement, stream, previews).
    /// Réservé au shutdown de l'application.
    /// </summary>
    public void StopAll()
    {
        StopRecording();
        StopStream();
        StopAllPreviews();
    }

    // ── Streaming ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Démarre un stream RTMP vers la plateforme donnée.
    /// Retourne 0 si succès, code d'erreur négatif sinon.
    /// </summary>
    public int StartStream(SceneItem scene, CastorServiceType service, string streamKeyOrUrl,
                           int fps = 30, int videoBitrateKbps = 4000)
    {
        if (IsStreaming) return -1;

        var videoItem = scene.Sources.FirstOrDefault(s => s.Type == "Vidéo" && s.Tag is CaptureSourceInfo);
        var audioItem = scene.Sources.FirstOrDefault(s => s.Type == "Audio" && s.Tag is AudioSourceInfo);

        if (videoItem == null) return -2;

        string? rtmpUrl = CastorNative.GetStreamingUrl(service, streamKeyOrUrl);
        System.Diagnostics.Debug.WriteLine($"[Stream] service={service} key='{streamKeyOrUrl}' → url='{rtmpUrl}'");
        if (string.IsNullOrEmpty(rtmpUrl)) return -4;

        var videoSrc = (CaptureSourceInfo)videoItem.Tag!;
        var audioSrc = audioItem != null ? (AudioSourceInfo)audioItem.Tag! : default;

        System.Diagnostics.Debug.WriteLine($"[Stream] RecorderCreate → scene='{scene.Name}'" +
            $", src.Type={videoSrc.Type}, hwnd=0x{videoSrc.Hwnd:X}, hmonitor=0x{videoSrc.HMonitor:X}" +
            $", audioItem={(audioItem != null ? audioSrc.Label : "none")}");

        var config = new RecorderConfig
        {
            Streams    = new StreamConfig[8],
            NumStreams = 1,
            Fps        = fps
        };

        config.Streams[0] = new StreamConfig
        {
            VideoSrc = videoSrc,
            AudioSrc = audioSrc,
            Output   = new OutputConfig
            {
                Type             = CastorOutputType.Rtmp,
                Destination      = rtmpUrl,
                VideoBitrateKbps = videoBitrateKbps > 0 ? videoBitrateKbps : 4000,
                AudioBitrateKbps = 128,
                GopSeconds       = 2,
                VideoCodec       = CastorVideoCodec.H264,
                AudioCodec       = CastorAudioCodec.AAC,
            }
        };

        _streamPtr = CastorNative.RecorderCreate(ref config);
        System.Diagnostics.Debug.WriteLine($"[Stream] RecorderCreate retourné : ptr=0x{_streamPtr:X}");
        if (_streamPtr == IntPtr.Zero) return -3;

        int result = CastorNative.RecorderStart(_streamPtr);
        System.Diagnostics.Debug.WriteLine($"[Stream] RecorderStart retourné : {result}");
        if (result != 0)
        {
            CastorNative.RecorderDestroy(_streamPtr);
            _streamPtr = IntPtr.Zero;
            return result;
        }

        StreamingStarted?.Invoke();
        return 0;
    }

    /// <summary>Arrête le stream RTMP en cours.</summary>
    public void StopStream()
    {
        if (_streamPtr == IntPtr.Zero) return;
        CastorNative.RecorderStop(_streamPtr);
        CastorNative.RecorderDestroy(_streamPtr);
        _streamPtr = IntPtr.Zero;
        StreamingStopped?.Invoke();
    }

    // ── Preview local (MediaMTX) — un flux indépendant par scène ─────────────

    /// <summary>
    /// Démarre un flux de preview vers MediaMTX pour une scène donnée.
    /// Chaque scène pousse sur rtmp://127.0.0.1:1935/live/{sceneId}.
    ///
    /// Thread-safe : si deux threads appellent StartPreview pour la même scène
    /// simultanément, le second voit le flux déjà actif et retourne 0 sans rien créer.
    /// </summary>
    public int StartPreview(SceneItem scene, int fps = 30)
    {
        // Sérialisation : une seule opération native preview à la fois
        _previewSemaphore.Wait();
        try
        {
            // Re-check après acquisition du sémaphore (TOCTOU guard) :
            // un autre thread a peut-être déjà démarré le preview entre le
            // IsPreviewActive(id) externe et l'entrée dans ce bloc.
            lock (_previewLock)
            {
                if (_previewPtrs.TryGetValue(scene.Id, out var existing) && existing != IntPtr.Zero)
                {
                    System.Diagnostics.Debug.WriteLine($"[Preview] '{scene.Name}' déjà actif — ignoré.");
                    return 0;
                }
            }

            var videoItem = scene.Sources.FirstOrDefault(s => s.Type == "Vidéo" && s.Tag is CaptureSourceInfo);
            var audioItem = scene.Sources.FirstOrDefault(s => s.Type == "Audio" && s.Tag is AudioSourceInfo);

            if (videoItem == null) return -2;

            var videoSrc = (CaptureSourceInfo)videoItem.Tag!;
            var audioSrc = audioItem != null ? (AudioSourceInfo)audioItem.Tag! : default;

            var config = new RecorderConfig
            {
                Streams   = new StreamConfig[8],
                NumStreams = 1,
                Fps       = fps
            };

            config.Streams[0] = new StreamConfig
            {
                VideoSrc = videoSrc,
                AudioSrc = audioSrc,
                Output   = new OutputConfig
                {
                    Type             = CastorOutputType.Rtmp,
                    Destination      = MediaMtxService.GetPreviewPushUrl(scene.Id),
                    VideoBitrateKbps = 3000,
                    AudioBitrateKbps = 128,
                    // GopSeconds = 0 : en mode zerolatency, VideoEncode.c utilise
                    // fps/2 frames (0,5 s à 30 fps) → IDR plus fréquents, VLC
                    // peut rejoindre le flux plus vite après une reconnexion.
                    GopSeconds       = 0,
                }
            };

            System.Diagnostics.Debug.WriteLine(
                $"[Preview] RecorderCreate → scène='{scene.Name}'" +
                $", src.Type={videoSrc.Type}" +
                $", hwnd=0x{videoSrc.Hwnd:X}" +
                $", hmonitor=0x{videoSrc.HMonitor:X}" +
                $", url={MediaMtxService.GetPreviewPushUrl(scene.Id)}");

            var ptr = CastorNative.RecorderCreate(ref config);
            if (ptr == IntPtr.Zero) return -3;

            System.Diagnostics.Debug.WriteLine($"[Preview] RecorderStart → ptr=0x{ptr:X}");
            int result = CastorNative.RecorderStart(ptr);
            if (result != 0)
            {
                System.Diagnostics.Debug.WriteLine($"[Preview] RecorderStart retourné : {result} — destruction du recorder.");
                CastorNative.RecorderDestroy(ptr);
                return result;
            }

            lock (_previewLock)
                _previewPtrs[scene.Id] = ptr;

            System.Diagnostics.Debug.WriteLine($"[Preview] '{scene.Name}' démarré → {MediaMtxService.GetPreviewPushUrl(scene.Id)}");
            return 0;
        }
        finally
        {
            _previewSemaphore.Release();
        }
    }

    /// <summary>Arrête le flux de preview d'une scène spécifique.</summary>
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

    /// <summary>Arrête tous les flux de preview actifs.</summary>
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
            foreach (var (_, ptr) in copy)
            {
                if (ptr == IntPtr.Zero) continue;
                CastorNative.RecorderStop(ptr);
                CastorNative.RecorderDestroy(ptr);
            }
        }
        finally
        {
            _previewSemaphore.Release();
        }
    }

    /// <summary>Stop interne sans prise du sémaphore (appelé depuis un bloc déjà protégé).</summary>
    private void StopPreviewUnsafe(Guid sceneId)
    {
        IntPtr ptr;
        lock (_previewLock)
        {
            if (!_previewPtrs.TryGetValue(sceneId, out ptr) || ptr == IntPtr.Zero) return;
            _previewPtrs.Remove(sceneId);
        }
        CastorNative.RecorderStop(ptr);
        CastorNative.RecorderDestroy(ptr);
    }

    /// <summary>
    /// Change la source vidéo à la volée sur les recorders actifs (enregistrement et stream).
    /// Sérialisé via _switchSemaphore : si deux switches rapides arrivent, le second
    /// attend la fin du premier pour éviter un appel concurrent sur le même pointeur natif.
    /// </summary>
    public int SwitchScene(SceneItem scene)
    {
        _switchSemaphore.Wait();
        try
        {
            var videoItem = scene.Sources.FirstOrDefault(s => s.Type == "Vidéo" && s.Tag is CaptureSourceInfo);
            if (videoItem == null) return -1;

            var videoSrc = (CaptureSourceInfo)videoItem.Tag!;
            int result = 0;

            if (IsRecording)
                result = CastorNative.RecorderSwitchVideoSource(_recorderPtr, streamIndex: 0, videoSrc);

            if (IsStreaming && result == 0)
                result = CastorNative.RecorderSwitchVideoSource(_streamPtr, streamIndex: 0, videoSrc);

            return result;
        }
        finally
        {
            _switchSemaphore.Release();
        }
    }
}
