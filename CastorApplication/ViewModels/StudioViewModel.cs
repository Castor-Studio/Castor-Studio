using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform.Storage;
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

            // Switch de source vidéo à la volée si un enregistrement est en cours
            if (IsRecording)
                RecorderService.Instance.SwitchScene(value);
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

    // ── Streaming commands ──

    [RelayCommand]
    private void StartStreaming() => IsStreaming = true;

    [RelayCommand]
    private void StopStreaming() => IsStreaming = false;

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

