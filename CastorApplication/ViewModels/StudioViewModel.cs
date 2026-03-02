using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using Avalonia.Media;
using CastorApplication.Factories;
using CastorApplication.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Serializer;

namespace CastorApplication.ViewModels;

public partial class StudioViewModel : ViewModelBase
{
    // ── Docking layout ──

    [ObservableProperty]
    private IRootDock? _layout;

    // ── Scene selection (F4 - Scene Switching) ──

    public ObservableCollection<string> SceneNames { get; } = new()
    {
        "Match Live",
        "Introduction",
        "Mi-Temps",
        "Fin de Match"
    };

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
    private int _recordFormatIndex;

    public string RecordStatusText => IsRecording ? "REC" : "";

    partial void OnIsRecordingChanged(bool value)
    {
        OnPropertyChanged(nameof(RecordStatusText));
    }

    // ── Constructor ──

    public StudioViewModel()
    {
        // Sample sources
        Sources.Add(new SourceItem("Capture d'écran", "Vidéo", "#5b8def"));
        Sources.Add(new SourceItem("Caméra principale", "Vidéo", "#34d399"));
        Sources.Add(new SourceItem("Capture de jeu", "Vidéo", "#3c3c4e") { IsActive = false });

        // Docking layout
        var factory = new StudioDockFactory(this);

        if (File.Exists("layout.json"))
        {
            LoadLayout(factory);
        }

        if (Layout == null)
        {
            Layout = factory.CreateLayout();
            if (Layout != null)
            {
                factory.InitLayout(Layout);
                SaveLayout();
            }
        }

        if (Layout is INotifyPropertyChanged notify)
        {
            notify.PropertyChanged += (_, _) => SaveLayout();
        }
    }

    // ── Docking commands ──

    public void SaveLayout()
    {
        if (Layout != null)
        {
            var serializer = new DockSerializer();
            var json = serializer.Serialize(Layout);
            File.WriteAllText("layout.json", json);
        }
    }

    public void LoadLayout(IFactory factory)
    {
        if (File.Exists("layout.json"))
        {
            var json = File.ReadAllText("layout.json");
            var serializer = new DockSerializer();
            var layout = serializer.Deserialize<IRootDock>(json);

            if (layout != null)
            {
                Layout = layout;
                factory.InitLayout(Layout);
            }
        }
    }

    // ── Streaming commands ──

    [RelayCommand]
    private void StartStreaming() => IsStreaming = true;

    [RelayCommand]
    private void StopStreaming() => IsStreaming = false;

    // ── Recording commands ──

    [RelayCommand]
    private void StartRecording() => IsRecording = true;

    [RelayCommand]
    private void StopRecording() => IsRecording = false;

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
