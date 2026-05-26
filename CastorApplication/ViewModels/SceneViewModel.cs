using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Castor.Native;
using CastorApplication.Models;
using CastorApplication.Services;
using CastorApplication.Services.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.ViewModels;

public partial class ScenesViewModel : ViewModelBase
{
    // ── Scènes ────────────────────────────────────────────────────────────────

    public ObservableCollection<SceneItem> Scenes => SceneService.Instance.Scenes;

    [ObservableProperty]
    private SceneItem? _selectedScene;

    [ObservableProperty]
    private string _newSceneName = "";

    // ── Sources disponibles (pour le flyout d'ajout) ──────────────────────────

    public ObservableCollection<CaptureSourceOption> AvailableMonitors  { get; } = new();
    public ObservableCollection<CaptureSourceOption> AvailableCameras   { get; } = new();
    public ObservableCollection<CaptureSourceOption> AvailableWindows   { get; } = new();
    public ObservableCollection<AudioSourceOption>   AvailableLoopbacks { get; } = new();
    public ObservableCollection<AudioSourceOption>   AvailableMics      { get; } = new();

    // ── Player preview (volume + mute auto) ─────────────────────────────────

    [ObservableProperty]
    private double _playerVolume = 80;

    [ObservableProperty]
    private bool _mutePlayersOnRecord = true;

    private double _savedPlayerVolume = 80;
    private bool _mutedForCapture;

    private void ApplyAutoMute()
    {
        if (!MutePlayersOnRecord || _mutedForCapture) return;
        _savedPlayerVolume = PlayerVolume;
        PlayerVolume       = 0;
        _mutedForCapture   = true;
    }

    private void RestoreAutoMute()
    {
        if (!_mutedForCapture) return;
        if (RecorderService.Instance.IsRecording || RecorderService.Instance.IsStreaming) return;
        PlayerVolume     = _savedPlayerVolume;
        _mutedForCapture = false;
    }

    // ── Constructeur ─────────────────────────────────────────────────────────

    public ScenesViewModel(SettingsService settingsService)
    {
        var settings     = settingsService.Load();
        _playerVolume        = settings.PlayerVolume;
        _mutePlayersOnRecord = settings.MutePlayersOnRecord;
        _savedPlayerVolume   = _playerVolume;

        RecorderService.Instance.RecordingStarted  += ApplyAutoMute;
        RecorderService.Instance.RecordingStopped  += RestoreAutoMute;
        RecorderService.Instance.StreamingStarted  += ApplyAutoMute;
        RecorderService.Instance.StreamingStopped  += RestoreAutoMute;

        // Synchronise la sélection UI avec la scène active du service
        SelectedScene = SceneService.Instance.ActiveScene;

        // Charge les sources disponibles depuis la DLL
        LoadAvailableSources();
    }

    private void LoadAvailableSources()
    {
        try
        {
            foreach (var src in CastorNative.ListVideoSources())
            {
                var opt = new CaptureSourceOption(src);
                switch (src.Type)
                {
                    case CaptureSourceType.Monitor: AvailableMonitors.Add(opt);  break;
                    case CaptureSourceType.Camera:  AvailableCameras.Add(opt);   break;
                    case CaptureSourceType.Window:  AvailableWindows.Add(opt);   break;
                }
            }

            foreach (var src in CastorNative.ListAudioSources())
            {
                var opt = new AudioSourceOption(src);
                if (src.Type is AudioSourceType.Microphone or AudioSourceType.CameraMic)
                    AvailableMics.Add(opt);
                else
                    AvailableLoopbacks.Add(opt);
            }
        }
        catch { /* DLL non chargée en mode design */ }
    }

    // ── Commandes scènes ──────────────────────────────────────────────────────

    [RelayCommand]
    private void SelectScene(SceneItem scene)
    {
        SceneService.Instance.SetActiveScene(scene);
        SelectedScene = scene;
    }

    [RelayCommand]
    private void CreateScene()
    {
        if (string.IsNullOrWhiteSpace(NewSceneName)) return;

        var scene = SceneService.Instance.CreateScene(NewSceneName.Trim());
        NewSceneName = "";
        SelectScene(scene);
    }

    [RelayCommand]
    private void DeleteScene(SceneItem scene) => SceneService.Instance.DeleteScene(scene);

    // ── Flux réseau ───────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _networkSourceUrl = "";

    [ObservableProperty]
    private string _networkSourceError = "";

    [ObservableProperty]
    private bool _isScanning;

    public ObservableCollection<DiscoveredCamera> DiscoveredCameras { get; } = new();

    // ── Commandes d'ajout de sources ─────────────────────────────────────────

    /// <summary>Ajoute une source vidéo spécifique à la scène sélectionnée.</summary>
    [RelayCommand]
    private void AddSpecificVideoSource(CaptureSourceOption opt)
    {
        if (SelectedScene == null) return;
        SceneService.Instance.AddVideoSource(SelectedScene, opt.Info);
    }

    /// <summary>Scanne le réseau local via WS-Discovery pour trouver des caméras ONVIF.</summary>
    [RelayCommand]
    private async Task ScanNetworkCameras()
    {
        if (IsScanning) return;
        IsScanning = true;
        NetworkSourceError = "";
        DiscoveredCameras.Clear();

        try
        {
            var found = await NetworkScanService.ScanAsync(TimeSpan.FromSeconds(3));
            foreach (var cam in found)
                DiscoveredCameras.Add(cam);

            if (DiscoveredCameras.Count == 0)
                NetworkSourceError = "Aucune caméra ONVIF trouvée sur le réseau.";
        }
        catch (Exception ex)
        {
            NetworkSourceError = $"Erreur scan : {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    /// <summary>Ajoute une caméra découverte à la scène.</summary>
    [RelayCommand]
    private void AddDiscoveredCamera(DiscoveredCamera cam)
    {
        if (SelectedScene == null) return;
        var info = new CaptureSourceInfo
        {
            Label        = cam.Label,
            Type         = CaptureSourceType.Network,
            SymbolicLink = cam.SuggestedUrl,
            Index        = -1,
        };
        SceneService.Instance.AddVideoSource(SelectedScene, info);
    }

    /// <summary>Ajoute un flux réseau RTMP/RTSP/HTTP à la scène sélectionnée.</summary>
    [RelayCommand]
    private void AddNetworkSource()
    {
        NetworkSourceError = "";
        var url = NetworkSourceUrl.Trim();

        if (string.IsNullOrEmpty(url))
        {
            NetworkSourceError = "Entrez une URL valide.";
            return;
        }

        if (!url.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("rtmps://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            NetworkSourceError = "URL invalide (rtmp://, rtsp://, http://)";
            return;
        }

        if (SelectedScene == null)
        {
            NetworkSourceError = "Sélectionnez une scène d'abord.";
            return;
        }

        // Construit un CaptureSourceInfo de type Network
        var info = new CaptureSourceInfo
        {
            Label        = url.Length > 30 ? url[..30] + "…" : url,
            Type         = CaptureSourceType.Network,
            SymbolicLink = url,
            Index        = -1,
        };

        SceneService.Instance.AddVideoSource(SelectedScene, info);
        NetworkSourceUrl = "";
    }

    /// <summary>Ajoute une source audio spécifique à la scène sélectionnée.</summary>
    [RelayCommand]
    private void AddSpecificAudioSource(AudioSourceOption opt)
    {
        if (SelectedScene == null) return;
        SceneService.Instance.AddAudioSource(SelectedScene, opt.Info);
    }

    // ── Suppression de source ─────────────────────────────────────────────────

    [RelayCommand]
    private void RemoveSource(SourceItem source)
    {
        if (SelectedScene == null) return;
        SceneService.Instance.RemoveSource(SelectedScene, source);
    }
}
