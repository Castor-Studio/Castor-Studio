using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CastorApplication.Models;
using CastorApplication.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media.Imaging;
using LibVLCSharp.Shared;

namespace CastorApplication.ViewModels;

public partial class MulticamViewModel : ViewModelBase
{
    private readonly PodcastAiService _ai = PodcastAiService.Instance;

    // ── Camera list (shared singleton so state survives tab switches) ────────

    public ObservableCollection<CameraItem> Cameras => MulticamSourcesService.Instance.Cameras;

    // ── AI Mode (F7/F8) ──────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isAiOff = true;

    [ObservableProperty]
    private bool _isAiAgent;

    [ObservableProperty]
    private bool _isAiAuto;

    [ObservableProperty]
    private int _selectedAiModelIndex;

    public bool IsAiEnabled => !IsAiOff;

    // ── AI status (visible in header bar) ────────────────────────────────────

    [ObservableProperty]
    private string _aiStatus = "";

    [ObservableProperty]
    private bool _isAiRunning;

    // ── Live AI output (the BIG view on the Multicam page) ───────────────────

    /// <summary>MediaPlayer that drives the big "AI output" view at the top
    /// of the Multicam page. Swaps media every time the AI emits a switch.</summary>
    [ObservableProperty]
    private MediaPlayer? _activeMediaPlayer;

    /// <summary>Static thumbnail of the currently-active source — used when VLC is unavailable.</summary>
    [ObservableProperty]
    private Bitmap? _activeThumbnail;

    // ── Empty state ──────────────────────────────────────────────────────────

    public bool HasCameras => Cameras.Count > 0;
    public bool HasNoCameras => Cameras.Count == 0;

    // ── Constructor ──────────────────────────────────────────────────────────

    public MulticamViewModel()
    {
        Cameras.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasCameras));
            OnPropertyChanged(nameof(HasNoCameras));
        };

        // Re-sync UI state from the (singleton) AI service so navigating back
        // to this tab while the AI is still running shows the correct state.
        if (_ai.IsRunning)
        {
            IsAiOff     = false;
            IsAiAuto    = true;
            IsAiRunning = true;
            AiStatus    = "IA en cours…";
        }

        _ai.Failed         += msg => Dispatcher.UIThread.Post(() => AiStatus = msg);
        _ai.RunningChanged += running => Dispatcher.UIThread.Post(() =>
        {
            IsAiRunning = running;
            if (!running)
            {
                if (IsAiEnabled) AiStatus = "IA arrêtée";
                StopActivePlayer();
            }
            else
            {
                // Kick off the big-view playback on the first source as soon
                // as the AI starts (before its first real switch).
                EnsureActivePlayer(_ai.CurrentSourceUri);
            }
        });
        _ai.ActiveSourceChanged += idx => Dispatcher.UIThread.Post(() =>
        {
            if (idx >= 0 && idx < Cameras.Count)
            {
                AiStatus = $"Active : {Cameras[idx].Label} — {Cameras[idx].Name}";
                ActiveThumbnail = Cameras[idx].Thumbnail;
            }
            EnsureActivePlayer(_ai.CurrentSourceUri);
        });
    }

    private void EnsureActivePlayer(string? uri)
    {
        if (string.IsNullOrEmpty(uri)) return;
        var libvlc = VlcService.Instance.LibVLC;
        if (libvlc == null) return;
        try
        {
            if (ActiveMediaPlayer == null)
            {
                ActiveMediaPlayer = VlcService.Instance.CreateLoopedPlayer(uri, mute: false);
                return;
            }
            // Swap media on the existing player (smoother than recreating).
            Media media = File.Exists(uri)
                ? new Media(libvlc, new Uri(uri, UriKind.Absolute), ":input-repeat=65535")
                : new Media(libvlc, new Uri(uri), ":network-caching=150");
            ActiveMediaPlayer.Play(media);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Multicam] EnsureActivePlayer failed: {ex.Message}");
        }
    }

    private void StopActivePlayer()
    {
        var p = ActiveMediaPlayer;
        ActiveMediaPlayer = null;
        if (p == null) return;
        try { if (p.IsPlaying) p.Stop(); } catch { }
        try { p.Dispose(); } catch { }
    }

    // ── Frame cycling timer (fakes video playback when VLC isn't available) ──

    private System.Threading.Timer? _frameTimer;
    private int _frameTick;

    private void StartFrameCycleTimer()
    {
        StopFrameCycleTimer();
        // 10 FPS playback (matches the extraction rate) — covers full video duration.
        _frameTimer = new System.Threading.Timer(_ =>
        {
            _frameTick++;
            Dispatcher.UIThread.Post(() =>
            {
                foreach (var cam in Cameras)
                {
                    var seq = cam.FrameSequence;
                    if (seq == null || seq.Count == 0) continue;
                    cam.Thumbnail = seq[_frameTick % seq.Count];
                }
                var active = Cameras.FirstOrDefault(c => c.IsActive);
                if (active?.Thumbnail != null) ActiveThumbnail = active.Thumbnail;
            });
        }, null, 100, 100);
    }

    private void StopFrameCycleTimer()
    {
        var t = _frameTimer;
        _frameTimer = null;
        try { t?.Dispose(); } catch { }
    }

    // ── Camera commands ──────────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoadVideoFiles()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;
        var topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title          = "Choisir les vidéos podcast (≥ 2)",
            AllowMultiple  = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Vidéo")
                {
                    Patterns = ["*.mp4", "*.mov", "*.mkv", "*.avi", "*.webm"],
                }
            ],
        });

        if (files == null || files.Count == 0) return;

        StopAiInternal();
        foreach (var cam in Cameras) cam.DisposePlayer();
        Cameras.Clear();

        int index = 1;
        foreach (var f in files)
        {
            var path = f.Path.LocalPath;
            if (!File.Exists(path)) continue;
            var player = VlcService.Instance.LibVLC != null
                ? VlcService.Instance.CreateLoopedPlayer(path, mute: true)
                : null;
            var cam = new CameraItem(
                label:       $"CAM {index}",
                name:        Path.GetFileNameWithoutExtension(path),
                isActive:    index == 1,
                isLive:      index == 1,
                sourceUri:   path,
                mediaPlayer: player);
            Cameras.Add(cam);
            index++;
        }

        // Extract animated previews OFF the UI thread (heavy ffmpeg work).
        if (VlcService.Instance.LibVLC == null)
        {
            AiStatus = "Extraction des previews… (~5 s)";
            await Task.Run(() =>
            {
                foreach (var cam in Cameras)
                {
                    if (string.IsNullOrEmpty(cam.SourceUri)) continue;
                    var seq = ThumbnailService.ExtractSequence(cam.SourceUri!, fps: 10, maxFrames: 800);
                    cam.FrameSequence = seq;
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (seq.Count > 0) cam.Thumbnail = seq[0];
                    });
                }
            });
            ActiveThumbnail = Cameras.FirstOrDefault()?.Thumbnail;
            StartFrameCycleTimer();
        }

        // Preload BIG view with cam 1's thumbnail so the Studio area isn't blank.
        ActiveThumbnail = Cameras.FirstOrDefault()?.Thumbnail;

        AiStatus = Cameras.Count >= 2
            ? $"{Cameras.Count} sources prêtes — clique MODE AUTO pour démarrer l'IA."
            : "Importe au moins 2 vidéos.";

        AiStatus = Cameras.Count >= 2
            ? $"{Cameras.Count} sources prêtes — lance l'IA en mode AUTO."
            : "Sélectionne au moins 2 vidéos pour activer l'IA podcast.";
    }

    [RelayCommand]
    private void ClearCameras()
    {
        StopAiInternal();
        StopFrameCycleTimer();
        foreach (var cam in Cameras) cam.DisposePlayer();
        Cameras.Clear();
        ActiveThumbnail = null;
        AiStatus = "";
    }

    [RelayCommand]
    private void SelectCamera(CameraItem camera)
    {
        // Manual override: pick a camera explicitly (useful in Agent mode or off).
        foreach (var cam in Cameras)
        {
            cam.IsActive = false;
            cam.IsLive   = false;
        }
        camera.IsActive = true;
        camera.IsLive   = true;
    }

    private void OnAiActiveSourceChanged(int index)
    {
        // Kept for completeness — the actual IsActive/IsLive sync is done
        // by MulticamSourcesService so it survives tab switches.
    }

    // ── AI Mode commands ─────────────────────────────────────────────────────

    [RelayCommand]
    private void SetAiOff()
    {
        IsAiOff   = true;
        IsAiAgent = false;
        IsAiAuto  = false;
        OnPropertyChanged(nameof(IsAiEnabled));
        StopAiInternal();
        AiStatus = "";
    }

    [RelayCommand]
    private void SetAiAgent()
    {
        // Agent mode = AI suggests but the user clicks. For the demo we run
        // the same backend pipeline; the difference is just that the UI
        // doesn't auto-switch (left as a follow-up).
        IsAiOff   = false;
        IsAiAgent = true;
        IsAiAuto  = false;
        OnPropertyChanged(nameof(IsAiEnabled));
        StopAiInternal();
        AiStatus = "Mode Agent — bientôt disponible. Pour la démo, utilise Mode Auto.";
    }

    [RelayCommand]
    private void SetAiAuto()
    {
        IsAiOff   = false;
        IsAiAgent = false;
        IsAiAuto  = true;
        OnPropertyChanged(nameof(IsAiEnabled));
        StartAiInternal();
    }

    // ── AI lifecycle ─────────────────────────────────────────────────────────

    private void StartAiInternal()
    {
        var sources = Cameras
            .Where(c => !string.IsNullOrEmpty(c.SourceUri))
            .Select(c => c.SourceUri!)
            .ToList();

        if (sources.Count < 2)
        {
            AiStatus = "Sélectionne ≥ 2 vidéos avant de lancer l'IA.";
            return;
        }

        AiStatus = $"Lancement de l'IA podcast sur {sources.Count} sources…";
        _ai.Start(sources);
    }

    private void StopAiInternal()
    {
        if (_ai.IsRunning) _ai.Stop();
    }

}
