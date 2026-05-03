using System;
using System.Collections.ObjectModel;
using Castor.Native;
using CastorApplication.Models;

namespace CastorApplication.Services;

/// <summary>
/// Singleton qui gère la liste des scènes et leur état natif.
/// Partagé entre StudioViewModel et ScenesViewModel.
/// </summary>
public sealed class SceneService
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    private static SceneService? _instance;
    public static SceneService Instance => _instance ??= new SceneService();

    // ── État ──────────────────────────────────────────────────────────────────
    public ObservableCollection<SceneItem> Scenes { get; } = new();

    private SceneItem? _activeScene;
    public SceneItem? ActiveScene
    {
        get => _activeScene;
        set
        {
            if (_activeScene == value) return;
            if (_activeScene != null) _activeScene.IsActive = false;
            _activeScene = value;
            if (_activeScene != null) _activeScene.IsActive = true;
        }
    }

    // ── Constructeur privé ────────────────────────────────────────────────────
    private SceneService() { }

    // ── Gestion des scènes ────────────────────────────────────────────────────

    public SceneItem CreateScene(string name)
    {
        // NativePtr sera initialisé au démarrage de l'enregistrement/stream
        var scene = new SceneItem(name);
        Scenes.Add(scene);

        if (ActiveScene == null)
            ActiveScene = scene;

        return scene;
    }

    public void DeleteScene(SceneItem scene)
    {
        if (Scenes.Count <= 1) return;

        // Libère les ressources natives si elles ont été allouées (lors d'un enregistrement)
        foreach (var src in scene.Sources)
        {
            if (src.NativePtr != IntPtr.Zero)
            {
                CastorNative.NativeDeactivateSource(src.NativePtr);
                CastorNative.NativeDestroySource(src.NativePtr);
            }
        }

        Scenes.Remove(scene);

        if (ActiveScene == scene)
            ActiveScene = Scenes.Count > 0 ? Scenes[0] : null;
    }

    public void SetActiveScene(SceneItem scene)
    {
        if (!Scenes.Contains(scene)) return;
        ActiveScene = scene;
    }

    // ── Gestion des sources (composition — pas de capture active) ─────────────
    // Les pointeurs natifs sont créés plus tard, au démarrage du Recorder.

    public SourceItem AddVideoSource(SceneItem scene, CaptureSourceInfo info)
    {
        var color = info.Type switch
        {
            CaptureSourceType.Monitor => "#5b8def",
            CaptureSourceType.Camera  => "#34d399",
            CaptureSourceType.Window  => "#8888a0",
            _                         => "#5b8def"
        };

        // Stocke les infos ; NativePtr = Zero jusqu'au démarrage de l'enregistrement
        var item = new SourceItem(info.Label, "Vidéo", color) { Tag = info };
        scene.Sources.Add(item);
        return item;
    }

    public SourceItem AddAudioSource(SceneItem scene, AudioSourceInfo info)
    {
        var color = info.Type switch
        {
            AudioSourceType.Microphone or AudioSourceType.CameraMic => "#f87171",
            _ => "#fbbf24"
        };

        var item = new SourceItem(info.Label, "Audio", color) { Tag = info };
        scene.Sources.Add(item);
        return item;
    }

    public void RemoveSource(SceneItem scene, SourceItem source)
    {
        if (source.NativePtr != IntPtr.Zero)
        {
            CastorNative.NativeDeactivateSource(source.NativePtr);
            CastorNative.NativeDestroySource(source.NativePtr);
        }
        scene.Sources.Remove(source);
    }
}
