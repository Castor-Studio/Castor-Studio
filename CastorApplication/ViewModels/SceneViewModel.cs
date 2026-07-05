using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Castor.Engine.Models;
using Castor.Engine.Services;
using CastorApplication.Services;
using CastorApplication.Services.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.ViewModels;

public partial class ScenesViewModel : ViewModelBase
{
    private readonly IStudioController _studioController;
    private readonly INativeCaptureService _nativeCaptureService;
    private readonly INetworkCameraDiscoveryService _networkCameraDiscoveryService;
    private readonly IFilePickerService _filePickerService;

    public ObservableCollection<SceneItem> Scenes => _studioController.Scenes;

    [ObservableProperty]
    private SceneItem? _selectedScene;

    [ObservableProperty]
    private string _newSceneName = "";

    public ObservableCollection<CaptureSourceOption> AvailableMonitors { get; } = new();
    public ObservableCollection<CaptureSourceOption> AvailableCameras { get; } = new();
    public ObservableCollection<CaptureSourceOption> AvailableWindows { get; } = new();
    public ObservableCollection<AudioSourceOption> AvailableLoopbacks { get; } = new();
    public ObservableCollection<AudioSourceOption> AvailableMics { get; } = new();

    [ObservableProperty]
    private double _playerVolume = 80;

    [ObservableProperty]
    private bool _mutePlayersOnRecord = true;

    private double _savedPlayerVolume = 80;
    private bool _mutedForCapture;

    [ObservableProperty]
    private string _networkSourceUrl = "";

    [ObservableProperty]
    private string _networkSourceError = "";

    [ObservableProperty]
    private bool _isScanning;

    public ObservableCollection<DiscoveredCamera> DiscoveredCameras { get; } = new();

    public ScenesViewModel(
        SettingsService settingsService,
        IStudioController studioController,
        INativeCaptureService nativeCaptureService,
        INetworkCameraDiscoveryService networkCameraDiscoveryService,
        IFilePickerService filePickerService)
    {
        _studioController = studioController;
        _nativeCaptureService = nativeCaptureService;
        _networkCameraDiscoveryService = networkCameraDiscoveryService;
        _filePickerService = filePickerService;

        var settings = settingsService.Load();
        _playerVolume = settings.PlayerVolume;
        _mutePlayersOnRecord = settings.MutePlayersOnRecord;
        _savedPlayerVolume = _playerVolume;

        _studioController.RecordingStarted += ApplyAutoMute;
        _studioController.RecordingStopped += RestoreAutoMute;
        _studioController.StreamingStarted += ApplyAutoMute;
        _studioController.StreamingStopped += RestoreAutoMute;

        SelectedScene = _studioController.ActiveScene;

        LoadAvailableSources();
    }

    public bool IsPreviewActive(Guid sceneId) => _studioController.IsPreviewActive(sceneId);

    public int StartPreview(SceneItem scene) => _studioController.EnsurePreview(scene);

    public string GetPreviewPullUrl(Guid sceneId) => _studioController.GetPreviewPullUrl(sceneId);

    private void ApplyAutoMute()
    {
        if (!MutePlayersOnRecord || _mutedForCapture) return;
        _savedPlayerVolume = PlayerVolume;
        PlayerVolume = 0;
        _mutedForCapture = true;
    }

    private void RestoreAutoMute()
    {
        if (!_mutedForCapture) return;
        if (_studioController.IsRecording || _studioController.IsStreaming) return;
        PlayerVolume = _savedPlayerVolume;
        _mutedForCapture = false;
    }

    private void LoadAvailableSources()
    {
        try
        {
            foreach (var source in _nativeCaptureService.ListVideoSources())
            {
                switch (source.Type)
                {
                    case VideoCaptureKind.Monitor:
                        AvailableMonitors.Add(source);
                        break;
                    case VideoCaptureKind.Camera:
                        AvailableCameras.Add(source);
                        break;
                    case VideoCaptureKind.Window:
                        AvailableWindows.Add(source);
                        break;
                }
            }

            foreach (var source in _nativeCaptureService.ListAudioSources())
            {
                if (source.Type is AudioCaptureKind.Microphone or AudioCaptureKind.CameraMic)
                    AvailableMics.Add(source);
                else
                    AvailableLoopbacks.Add(source);
            }
        }
        catch
        {
            // Native discovery can fail in design mode.
        }
    }

    [RelayCommand]
    private void SelectScene(SceneItem scene)
    {
        _studioController.SelectScene(scene);
        SelectedScene = scene;
    }

    [RelayCommand]
    private void CreateScene()
    {
        if (string.IsNullOrWhiteSpace(NewSceneName)) return;

        var scene = _studioController.CreateScene(NewSceneName.Trim());
        NewSceneName = "";
        SelectScene(scene);
    }

    [ObservableProperty]
    private string _deleteSceneError = "";

    [RelayCommand]
    private void DeleteScene(SceneItem scene)
    {
        if (Scenes.Count <= 1 && (_studioController.IsRecording || _studioController.IsStreaming))
        {
            DeleteSceneError = "Impossible de supprimer la seule scène pendant un enregistrement ou un live.";
            return;
        }

        DeleteSceneError = "";
        var wasSelected = SelectedScene == scene;
        _studioController.DeleteScene(scene);
        if (wasSelected)
            SelectedScene = _studioController.ActiveScene;
    }

    [ObservableProperty]
    private SceneItem? _sceneBeingRenamed;

    [ObservableProperty]
    private string _renameSceneName = "";

    [RelayCommand]
    private void BeginRenameScene(SceneItem scene)
    {
        SceneBeingRenamed = scene;
        RenameSceneName = scene.Name;
    }

    [RelayCommand]
    private void ConfirmRenameScene()
    {
        if (SceneBeingRenamed == null || string.IsNullOrWhiteSpace(RenameSceneName)) return;
        _studioController.RenameScene(SceneBeingRenamed, RenameSceneName.Trim());
        SceneBeingRenamed = null;
    }

    [RelayCommand]
    private void AddSpecificVideoSource(CaptureSourceOption option)
    {
        if (SelectedScene == null) return;
        _studioController.AddVideoSource(SelectedScene, option);
    }

    [RelayCommand]
    private async Task ScanNetworkCameras()
    {
        if (IsScanning) return;
        IsScanning = true;
        NetworkSourceError = "";
        DiscoveredCameras.Clear();

        try
        {
            var found = await _networkCameraDiscoveryService.ScanAsync(TimeSpan.FromSeconds(3));
            foreach (var camera in found)
                DiscoveredCameras.Add(camera);

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

    [RelayCommand]
    private void AddDiscoveredCamera(DiscoveredCamera camera)
    {
        if (SelectedScene == null) return;
        _studioController.AddNetworkVideoSource(SelectedScene, camera.Label, camera.SuggestedUrl);
    }

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

        _studioController.AddNetworkVideoSource(SelectedScene, url.Length > 30 ? url[..30] + "…" : url, url);
        NetworkSourceUrl = "";
    }

    [RelayCommand]
    private void AddSpecificAudioSource(AudioSourceOption option)
    {
        if (SelectedScene == null) return;
        _studioController.AddAudioSource(SelectedScene, option);
    }

    [RelayCommand]
    private async Task AddFileVideoSource()
    {
        if (SelectedScene == null) return;

        var path = await _filePickerService.PickVideoFileAsync();
        if (path == null) return;

        _studioController.AddFileVideoSource(SelectedScene, new FileVideoSourceOption(path));
    }

    [RelayCommand]
    private async Task AddFileAudioSource()
    {
        if (SelectedScene == null) return;

        var path = await _filePickerService.PickAudioFileAsync();
        if (path == null) return;

        _studioController.AddFileAudioSource(SelectedScene, new FileAudioSourceOption(path));
    }

    /// <summary>
    /// Ajoute un fichier vidéo comme source vidéo ET comme source audio simultanément
    /// (deux SourceItem distincts depuis le même fichier).
    /// </summary>
    [RelayCommand]
    private async Task AddFileMediaSource()
    {
        if (SelectedScene == null) return;

        var path = await _filePickerService.PickVideoFileAsync();
        if (path == null) return;

        _studioController.AddFileVideoSource(SelectedScene, new FileVideoSourceOption(path));
        _studioController.AddFileAudioSource(SelectedScene, new FileAudioSourceOption(path));
    }

    [RelayCommand]
    private void RemoveSource(SourceItem source)
    {
        if (SelectedScene == null) return;
        _studioController.RemoveSource(SelectedScene, source);
    }
}
