using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Castor.Engine.Models;
using Castor.Engine.Services;
using CastorApplication.Models.Export;
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

    [ObservableProperty]
    private bool _isSelectionModeActive;

    partial void OnIsSelectionModeActiveChanged(bool value)
    {
        if (value) return;
        foreach (var scene in Scenes)
            scene.IsMultiSelected = false;
    }

    public ObservableCollection<CaptureSourceOption> AvailableMonitors { get; } = new();
    public ObservableCollection<CaptureSourceOption> AvailableCameras { get; } = new();
    public ObservableCollection<CaptureSourceOption> AvailableWindows { get; } = new();
    public ObservableCollection<AudioSourceOption> AvailableLoopbacks { get; } = new();
    public ObservableCollection<AudioSourceOption> AvailableMics { get; } = new();

    public ScenesViewModel(
        IStudioController studioController,
        INativeCaptureService nativeCaptureService,
        INetworkCameraDiscoveryService networkCameraDiscoveryService,
        IFilePickerService filePickerService)
    {
        _studioController = studioController;
        _nativeCaptureService = nativeCaptureService;
        _networkCameraDiscoveryService = networkCameraDiscoveryService;
        _filePickerService = filePickerService;

        SelectedScene = _studioController.ActiveScene;

        LoadAvailableSources();
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

        // On affiche la nouvelle scène dans le panneau d'édition sans basculer le live dessus
        // (SelectScene activerait aussi la scène côté recorder/preview).
        SelectedScene = scene;
    }

    [ObservableProperty]
    private string _deleteSceneError = "";

    /// <summary>Vrai si supprimer <paramref name="countToDelete"/> scène(s) viderait la liste pendant un enregistrement/live.</summary>
    private bool WouldLeaveNoScenesWhileLive(int countToDelete) =>
        countToDelete >= Scenes.Count && (_studioController.IsRecording || _studioController.IsStreaming);

    private List<SceneItem> GetSelectedScenes() => Scenes.Where(s => s.IsMultiSelected).ToList();

    [RelayCommand]
    private void DeleteScene(SceneItem scene)
    {
        if (WouldLeaveNoScenesWhileLive(1))
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

    [RelayCommand]
    private void DeleteSelectedScenes()
    {
        var selected = GetSelectedScenes();
        if (selected.Count == 0)
        {
            DeleteSceneError = "Sélectionnez au moins une scène à supprimer.";
            return;
        }

        if (WouldLeaveNoScenesWhileLive(selected.Count))
        {
            DeleteSceneError = "Impossible de supprimer toutes les scènes pendant un enregistrement ou un live.";
            return;
        }

        DeleteSceneError = "";
        foreach (var scene in selected)
        {
            var wasSelected = SelectedScene == scene;
            _studioController.DeleteScene(scene);
            if (wasSelected)
                SelectedScene = _studioController.ActiveScene;
        }
    }

    [RelayCommand]
    private void StartSelectedFileScenesTogether()
    {
        var selected = GetSelectedScenes();
        var fileScenes = selected.Where(s => s.Sources.Any(source => source.IsFileSource)).ToList();

        if (fileScenes.Count == 0)
        {
            SceneIoStatus = "Sélectionnez au moins une scène contenant une source fichier.";
            return;
        }

        // Sur un thread d'arrière-plan : RestartPreview arrête chaque scène de façon synchrone,
        // ce qui bloquerait l'UI si on l'appelait en série pour plusieurs scènes ici.
        _ = Task.Run(() =>
        {
            foreach (var scene in fileScenes)
                _studioController.RestartPreview(scene);
        });

        SceneIoStatus = fileScenes.Count == selected.Count
            ? $"{fileScenes.Count} scène(s) démarrée(s) ensemble."
            : $"{fileScenes.Count} scène(s) démarrée(s) ensemble ({selected.Count - fileScenes.Count} ignorée(s), sans source fichier).";
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

    public static IReadOnlyList<string> SceneColorPalette { get; } =
    [
        "#5b8def", "#34d399", "#f87171", "#fbbf24", "#a78bfa", "#fb923c", "#8888a0"
    ];

    [ObservableProperty]
    private SceneItem? _sceneBeingColored;

    [RelayCommand]
    private void BeginAssignColor(SceneItem scene) => SceneBeingColored = scene;

    [RelayCommand]
    private void AssignSceneColor(string color)
    {
        if (SceneBeingColored == null) return;
        SceneBeingColored.Color = color;
    }

    [RelayCommand]
    private void SortScenes(string sortKey)
    {
        IEnumerable<SceneItem> ordered = sortKey switch
        {
            "name_asc" => Scenes.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase),
            "name_desc" => Scenes.OrderByDescending(s => s.Name, StringComparer.CurrentCultureIgnoreCase),
            "date_asc" => Scenes.OrderBy(s => s.CreatedAt),
            "date_desc" => Scenes.OrderByDescending(s => s.CreatedAt),
            "color" => Scenes.OrderBy(s => s.Color, StringComparer.OrdinalIgnoreCase),
            _ => Scenes
        };

        var orderedScenes = ordered.ToList();
        for (var targetIndex = 0; targetIndex < orderedScenes.Count; targetIndex++)
        {
            var currentIndex = Scenes.IndexOf(orderedScenes[targetIndex]);
            if (currentIndex != targetIndex)
                Scenes.Move(currentIndex, targetIndex);
        }
    }

    private static readonly JsonSerializerOptions SceneExportJsonOptions = new() { WriteIndented = true };

    [ObservableProperty]
    private string _sceneIoStatus = "";

    [RelayCommand]
    private async Task ExportScenes()
    {
        var selected = GetSelectedScenes();
        var scenesToExport = selected.Count > 0 ? selected : Scenes.ToList();

        if (scenesToExport.Count == 0)
        {
            SceneIoStatus = "Aucune scène à exporter.";
            return;
        }

        var path = await _filePickerService.PickSceneExportFileAsync();
        if (path == null) return;

        try
        {
            var export = new SceneCollectionExport
            {
                Scenes = scenesToExport.Select(SceneExportMapper.ToExport).ToList()
            };
            var json = JsonSerializer.Serialize(export, SceneExportJsonOptions);
            SettingsService.WriteFileAtomically(path, json);

            SceneIoStatus = selected.Count > 0
                ? $"{scenesToExport.Count} scène(s) sélectionnée(s) exportée(s)."
                : $"{scenesToExport.Count} scène(s) exportée(s).";
        }
        catch (Exception ex)
        {
            SceneIoStatus = $"Export impossible : {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ImportScenes()
    {
        var path = await _filePickerService.PickSceneImportFileAsync();
        if (path == null) return;

        SceneCollectionExport? import;
        try
        {
            var json = await File.ReadAllTextAsync(path);
            import = JsonSerializer.Deserialize<SceneCollectionExport>(json);
        }
        catch (Exception ex)
        {
            SceneIoStatus = $"Import impossible : {ex.Message}";
            return;
        }

        if (import == null || import.Scenes.Count == 0)
        {
            SceneIoStatus = "Aucune scène trouvée dans ce fichier.";
            return;
        }

        var skippedSources = new List<string>();

        foreach (var sceneExport in import.Scenes)
        {
            var scene = _studioController.CreateScene(sceneExport.Name);
            scene.Color = sceneExport.Color;

            foreach (var sourceExport in sceneExport.Sources)
                ImportSource(scene, sourceExport, skippedSources);
        }

        SceneIoStatus = skippedSources.Count == 0
            ? $"{import.Scenes.Count} scène(s) importée(s)."
            : $"{import.Scenes.Count} scène(s) importée(s). Sources introuvables ignorées : {string.Join(", ", skippedSources)}";
    }

    private void ImportSource(SceneItem scene, SourceExport sourceExport, List<string> skippedSources)
    {
        switch (sourceExport.Origin)
        {
            case SourceOrigin.File when sourceExport.Kind == SourceKind.Video:
                _studioController.AddFileVideoSource(scene, new FileVideoSourceOption(sourceExport.OriginPath, sourceExport.Loop));
                break;

            case SourceOrigin.File:
                _studioController.AddFileAudioSource(scene, new FileAudioSourceOption(sourceExport.OriginPath, sourceExport.Loop));
                break;

            case SourceOrigin.Network:
                _studioController.AddNetworkVideoSource(scene, sourceExport.OriginLabel, sourceExport.OriginPath);
                break;

            case SourceOrigin.HardwareVideo:
                var videoMatch = AvailableMonitors.Concat(AvailableCameras).Concat(AvailableWindows)
                    .FirstOrDefault(o => o.Label == sourceExport.OriginLabel);
                if (videoMatch != null)
                    _studioController.AddVideoSource(scene, videoMatch);
                else
                    skippedSources.Add(sourceExport.Name);
                break;

            case SourceOrigin.HardwareAudio:
                var audioMatch = AvailableMics.Concat(AvailableLoopbacks)
                    .FirstOrDefault(o => o.Label == sourceExport.OriginLabel);
                if (audioMatch != null)
                    _studioController.AddAudioSource(scene, audioMatch);
                else
                    skippedSources.Add(sourceExport.Name);
                break;
        }
    }

    [RelayCommand]
    private void AssignColorToSelection(string color)
    {
        foreach (var scene in GetSelectedScenes())
            scene.Color = color;
    }

    // ── Dialogue « Ajouter une source » ──────────────────────────────────────

    /// <summary>Construit le ViewModel du dialogue d'ajout de source pour la
    /// scène sélectionnée. La vue (code-behind) se charge du ShowDialog.</summary>
    public AddSourceDialogViewModel CreateAddSourceDialog()
        => new(_nativeCaptureService, _networkCameraDiscoveryService, SelectedScene);

    /// <summary>Applique le résultat du dialogue à la scène sélectionnée en
    /// réutilisant les chemins d'ajout existants.</summary>
    public async Task ApplyAddSourceResultAsync(AddSourceResult result)
    {
        if (SelectedScene == null) return;

        switch (result)
        {
            case AddSourceResult.Video v:
                _studioController.AddVideoSource(SelectedScene, v.Option);
                break;
            case AddSourceResult.Audio a:
                _studioController.AddAudioSource(SelectedScene, a.Option);
                break;
            case AddSourceResult.Network n:
                _studioController.AddNetworkVideoSource(SelectedScene, n.Label, n.Url);
                break;
            case AddSourceResult.PickFileVideo:
                await AddFileVideoSourceCommand.ExecuteAsync(null);
                break;
            case AddSourceResult.PickFileAudio:
                await AddFileAudioSourceCommand.ExecuteAsync(null);
                break;
            case AddSourceResult.PickFileMedia:
                await AddFileMediaSourceCommand.ExecuteAsync(null);
                break;
        }
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
