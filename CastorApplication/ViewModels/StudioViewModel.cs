using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Castor.Native;
using CastorApplication.Factories;
using CastorApplication.Models;
using CastorApplication.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;

namespace CastorApplication.ViewModels;

public partial class StudioViewModel : ViewModelBase
{
    // ── Scene selection ──

    public ObservableCollection<SceneItem> Scenes => SceneService.Instance.Scenes;

    public SceneItem? ActiveScene
    {
        get => SceneService.Instance.ActiveScene;
        set
        {
            if (value == null) return;
            SceneService.Instance.SetActiveScene(value);
            OnPropertyChanged();

            // Switch de source vidéo à la volée (en background pour ne pas bloquer le UI)
            if (IsRecording || IsStreaming)
                _ = System.Threading.Tasks.Task.Run(() => RecorderService.Instance.SwitchScene(value));

            // Démarre le preview de la nouvelle scène si pas encore actif
            if (value.Sources.Any(s => s.Type == "Vidéo") && !RecorderService.Instance.IsPreviewActive(value.Id))
                _ = System.Threading.Tasks.Task.Run(() =>
                {
                    int r = RecorderService.Instance.StartPreview(value);
                    System.Diagnostics.Debug.WriteLine($"[Preview] StartPreview (scene switch) retourné : {r}");
                });
        }
    }

    [ObservableProperty]
    private int _selectedSceneIndex;

    // ── Sources ──

    public ObservableCollection<SourceItem> Sources { get; } = new();

    // ── Audio Mixer ──

    [ObservableProperty]
    private double _desktopVolume = 80;

    [ObservableProperty]
    private double _micVolume = 65;

    [ObservableProperty]
    private double _musicVolume = 30;

    public string DesktopVolumeDisplay => $"{(int)DesktopVolume}%";
    public string MicVolumeDisplay => $"{(int)MicVolume}%";
    public string MusicVolumeDisplay => $"{(int)MusicVolume}%";

    partial void OnDesktopVolumeChanged(double value) => OnPropertyChanged(nameof(DesktopVolumeDisplay));
    partial void OnMicVolumeChanged(double value) => OnPropertyChanged(nameof(MicVolumeDisplay));
    partial void OnMusicVolumeChanged(double value) => OnPropertyChanged(nameof(MusicVolumeDisplay));

    // ── Streaming state (F1) ──

    [ObservableProperty]
    private bool _isStreaming;

    [ObservableProperty]
    private int _streamSceneIndex;

    [ObservableProperty]
    private int _streamPlatformIndex;

    [ObservableProperty]
    private string _streamRtmpKey = "";

    public string StreamStatusText => IsStreaming ? "EN DIRECT" : "OFFLINE";
    public IBrush StreamStatusBrush => IsStreaming
        ? SolidColorBrush.Parse("#f87171")
        : SolidColorBrush.Parse("#3c3c4e");
    public IBrush StreamTimerBrush => IsStreaming
        ? SolidColorBrush.Parse("#f87171")
        : SolidColorBrush.Parse("#3c3c4e");

    partial void OnIsStreamingChanged(bool value)
    {
        OnPropertyChanged(nameof(StreamStatusText));
        OnPropertyChanged(nameof(StreamStatusBrush));
        OnPropertyChanged(nameof(StreamTimerBrush));
    }

    // ── Recording state (F2) ──

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private int _recordSceneIndex;

    [ObservableProperty]
    private string _recordError = "";

    public string RecordStatusText => IsRecording ? "REC" : "";

    partial void OnIsRecordingChanged(bool value)
    {
        OnPropertyChanged(nameof(RecordStatusText));
    }

    // ── Dock layout ──

    public IRootDock Layout { get; }

    // ── Constructor ──

    public StudioViewModel()
    {
        var factory = new StudioDockFactory(this);
        Layout = factory.CreateLayout();
        factory.InitLayout(Layout);
    }

    /// <summary>
    /// Démarre le preview si une scène vidéo est active et que le preview ne tourne pas déjà.
    /// Appelé à chaque fois que la vue Studio est affichée.
    /// </summary>
    public void EnsurePreviewRunning()
    {
        var scene = SceneService.Instance.ActiveScene;
        if (scene == null)
        {
            System.Diagnostics.Debug.WriteLine("[Preview] EnsurePreviewRunning : aucune scène active.");
            return;
        }

        var hasVideo = scene.Sources.Any(s => s.Type == "Vidéo");
        System.Diagnostics.Debug.WriteLine($"[Preview] EnsurePreviewRunning : scène='{scene.Name}', sources={scene.Sources.Count}, hasVideo={hasVideo}, isPreviewActive={RecorderService.Instance.IsPreviewActive(scene.Id)}");

        if (!hasVideo)
        {
            System.Diagnostics.Debug.WriteLine("[Preview] Aucune source vidéo dans la scène — preview ignoré.");
            return;
        }

        if (RecorderService.Instance.IsPreviewActive(scene.Id))
        {
            System.Diagnostics.Debug.WriteLine($"[Preview] '{scene.Name}' déjà actif (Studio) — ignoré.");
            return;
        }

        _ = System.Threading.Tasks.Task.Run(() =>
        {
            int result = RecorderService.Instance.StartPreview(scene);
            System.Diagnostics.Debug.WriteLine($"[Preview] StartPreview retourné : {result}");
        });
    }

    // ── Streaming error ──

    [ObservableProperty]
    private string _streamError = "";

    // ── Streaming commands ──

    [RelayCommand]
    private async Task StartStreaming()
    {
        StreamError = "";

        var scene = StreamSceneIndex >= 0 && StreamSceneIndex < Scenes.Count
            ? Scenes[StreamSceneIndex]
            : SceneService.Instance.ActiveScene;

        if (scene == null || scene.Sources.All(s => s.Type != "Vidéo"))
        {
            StreamError = "Aucune source vidéo dans la scène sélectionnée.";
            return;
        }

        // Mapping index UI flyout → CastorServiceType
        // 0=Twitch, 1=YouTube Live, 2=Facebook Live, 3=RTMP Manuel
        CastorServiceType service;
        string keyOrUrl = StreamRtmpKey;
        service = StreamPlatformIndex switch
        {
            0 => CastorServiceType.Twitch,
            1 => CastorServiceType.YouTube,
            _ => CastorServiceType.Custom   // Facebook ou RTMP Manuel
        };

        if (string.IsNullOrWhiteSpace(keyOrUrl))
        {
            StreamError = "Clé de stream ou URL RTMP manquante.";
            return;
        }

        // Lance le preview local si pas encore actif
        if (!RecorderService.Instance.IsPreviewActive(scene.Id))
            _ = Task.Run(() =>
            {
                int r = RecorderService.Instance.StartPreview(scene);
                System.Diagnostics.Debug.WriteLine($"[Preview] StartPreview (StartStreaming) retourné : {r}");
            });

        // streaming_service_get_url peut faire un appel HTTPS (Twitch) → thread BG
        int result = await Task.Run(() => RecorderService.Instance.StartStream(scene, service, keyOrUrl));

        if (result == 0)
            IsStreaming = true;
        else
            StreamError = result switch
            {
                -2 => "Aucune source vidéo dans la scène.",
                -3 => "Impossible de créer le recorder natif.",
                -4 => "Impossible de construire l'URL RTMP (clé invalide ?).",
                _  => $"Erreur streaming (code {result})."
            };
    }

    [RelayCommand]
    private void StopStreaming()
    {
        RecorderService.Instance.StopStream();
        IsStreaming = false;
    }

    // ── Recording commands ──

    [RelayCommand]
    private async Task StartRecording()
    {
        RecordError = "";

        var scene = SceneService.Instance.ActiveScene;
        if (scene == null || scene.Sources.All(s => s.Type != "Vidéo"))
        {
            RecordError = "Aucune source vidéo dans la scène active.";
            return;
        }

        var path = await PickOutputFileAsync();
        if (path == null) return; // annulé par l'utilisateur

        int result = RecorderService.Instance.Start(scene, path);
        if (result == 0)
        {
            IsRecording = true;
        }
        else
        {
            RecordError = result switch
            {
                -2 => "Aucune source vidéo dans la scène.",
                -3 => "Impossible de créer le recorder natif.",
                _  => $"Erreur recorder (code {result})."
            };
        }
    }

    [RelayCommand]
    private void StopRecording()
    {
        RecorderService.Instance.Stop();
        IsRecording = false;
    }

    private static async Task<string?> PickOutputFileAsync()
    {
        if (Application.Current?.ApplicationLifetime
            is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        var topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
        if (topLevel == null) return null;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title             = "Enregistrer la vidéo sous...",
            SuggestedFileName = $"Castor_{DateTime.Now:yyyyMMdd_HHmmss}",
            FileTypeChoices   =
            [
                new FilePickerFileType("MP4 (H.264 + AAC)") { Patterns = ["*.mp4"] },
                new FilePickerFileType("MKV (H.264 + AAC)") { Patterns = ["*.mkv"] },
            ]
        });

        return file?.Path.LocalPath;
    }

    // ── Source management ──

    [RelayCommand]
    private void AddSource()
    {
        Sources.Add(new SourceItem("Nouvelle source", "Vidéo", "#5b8def"));
    }

    [RelayCommand]
    private void RemoveSource(SourceItem source)
    {
        Sources.Remove(source);
    }
}

public sealed class SourcesPanelContext(StudioViewModel studio)
{
    public StudioViewModel Studio => studio;
}

public sealed class AudioPanelContext(StudioViewModel studio)
{
    public StudioViewModel Studio => studio;
}

