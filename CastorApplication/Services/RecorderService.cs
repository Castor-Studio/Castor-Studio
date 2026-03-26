using System;
using System.Linq;
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
    public bool IsRecording => _recorderPtr != IntPtr.Zero;

    private RecorderService() { }

    // ── API publique ──────────────────────────────────────────────────────────

    /// <summary>
    /// Démarre l'enregistrement de la scène vers outputPath.
    /// Retourne 0 si succès, code d'erreur négatif sinon.
    /// </summary>
    public int Start(SceneItem scene, string outputPath, int fps = 30)
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
                Type        = CastorOutputType.File,
                Destination = outputPath,
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

        return 0;
    }

    /// <summary>Arrête l'enregistrement en cours.</summary>
    public void Stop()
    {
        if (_recorderPtr == IntPtr.Zero) return;
        CastorNative.RecorderStop(_recorderPtr);
        CastorNative.RecorderDestroy(_recorderPtr);
        _recorderPtr = IntPtr.Zero;
    }
}
