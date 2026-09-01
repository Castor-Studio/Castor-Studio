using System.Collections.ObjectModel;
using System.ComponentModel;
using CastorApplication.Models.Studio;
using CastorApplication.Services;
using CastorApplication.Services.Dialogs;
using CastorApplication.Services.Studio;
using CastorApplication.ViewModels.Studio;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.ViewModels.Scenes;

public partial class ScenesViewModel : ViewModelBase
{
    private readonly StudioWorkspaceViewModel _workspace;
    private readonly IStudioRuntime _runtime;
    private readonly IFilePickerService _filePickerService;
    private readonly ISceneCollectionService _sceneCollectionService;
    private readonly IAddSourceDialogViewModelFactory _dialogFactory;
    private readonly IAddSourceDialogService _dialogService;

    public ObservableCollection<SceneItemViewModel> Scenes => _workspace.Scenes;

    [ObservableProperty] private SceneItemViewModel? _selectedScene;
    [ObservableProperty] private string _newSceneName = "";
    [ObservableProperty] private bool _isSelectionModeActive;
    [ObservableProperty] private string _deleteSceneError = "";
    [ObservableProperty] private SceneItemViewModel? _sceneBeingRenamed;
    [ObservableProperty] private string _renameSceneName = "";
    [ObservableProperty] private SceneItemViewModel? _sceneBeingColored;
    [ObservableProperty] private string _sceneIoStatus = "";

    public static IReadOnlyList<string> SceneColorPalette { get; } =
    [
        "#5b8def", "#34d399", "#f87171", "#fbbf24", "#a78bfa", "#fb923c", "#8888a0"
    ];

    internal ScenesViewModel(
        StudioWorkspaceViewModel workspace,
        IStudioRuntime runtime,
        IFilePickerService filePickerService,
        ISceneCollectionService sceneCollectionService,
        IAddSourceDialogViewModelFactory dialogFactory,
        IAddSourceDialogService dialogService)
    {
        _workspace = workspace;
        _runtime = runtime;
        _filePickerService = filePickerService;
        _sceneCollectionService = sceneCollectionService;
        _dialogFactory = dialogFactory;
        _dialogService = dialogService;
        SelectedScene = workspace.ActiveScene;
        workspace.PropertyChanged += OnWorkspacePropertyChanged;
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StudioWorkspaceViewModel.ActiveScene))
            SelectedScene = _workspace.ActiveScene;
    }

    partial void OnIsSelectionModeActiveChanged(bool value)
    {
        if (value) return;
        foreach (var scene in Scenes) scene.IsMultiSelected = false;
    }

    [RelayCommand]
    private void SelectScene(SceneItemViewModel scene)
    {
        _workspace.SelectScene(scene);
        SelectedScene = scene;
    }

    [RelayCommand]
    private void CreateScene()
    {
        if (string.IsNullOrWhiteSpace(NewSceneName)) return;
        SelectedScene = _workspace.CreateScene(NewSceneName);
        NewSceneName = "";
    }

    [RelayCommand]
    private void DeleteScene(SceneItemViewModel scene)
    {
        if (WouldLeaveNoScenesWhileLive(1))
        {
            DeleteSceneError = "Impossible de supprimer la seule scène pendant un enregistrement ou un live.";
            return;
        }

        DeleteSceneError = "";
        _workspace.DeleteScene(scene);
        SelectedScene = _workspace.ActiveScene;
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
        foreach (var scene in selected) _workspace.DeleteScene(scene);
        SelectedScene = _workspace.ActiveScene;
    }

    [RelayCommand]
    private void StartSelectedFileScenesTogether() => SceneIoStatus = _runtime.UnavailableMessage;

    [RelayCommand]
    private void BeginRenameScene(SceneItemViewModel scene)
    {
        SceneBeingRenamed = scene;
        RenameSceneName = scene.Name;
    }

    [RelayCommand]
    private void ConfirmRenameScene()
    {
        if (SceneBeingRenamed == null || string.IsNullOrWhiteSpace(RenameSceneName)) return;
        SceneBeingRenamed.Name = RenameSceneName.Trim();
        SceneBeingRenamed = null;
    }

    [RelayCommand]
    private void BeginAssignColor(SceneItemViewModel scene) => SceneBeingColored = scene;

    [RelayCommand]
    private void AssignSceneColor(string color)
    {
        if (SceneBeingColored != null) SceneBeingColored.Color = color;
    }

    [RelayCommand]
    private void SortScenes(string sortKey)
    {
        var ordered = sortKey switch
        {
            "name_asc" => Scenes.OrderBy(scene => scene.Name, StringComparer.CurrentCultureIgnoreCase),
            "name_desc" => Scenes.OrderByDescending(scene => scene.Name, StringComparer.CurrentCultureIgnoreCase),
            "date_asc" => Scenes.OrderBy(scene => scene.CreatedAt),
            "date_desc" => Scenes.OrderByDescending(scene => scene.CreatedAt),
            "color" => Scenes.OrderBy(scene => scene.Color, StringComparer.OrdinalIgnoreCase),
            _ => Scenes.AsEnumerable()
        };

        var result = ordered.ToList();
        for (var index = 0; index < result.Count; index++)
        {
            var current = Scenes.IndexOf(result[index]);
            if (current != index) Scenes.Move(current, index);
        }
    }

    [RelayCommand]
    private async Task ExportScenes(CancellationToken cancellationToken)
    {
        var selected = GetSelectedScenes();
        var scenes = selected.Count > 0 ? selected : Scenes.ToList();
        if (scenes.Count == 0)
        {
            SceneIoStatus = "Aucune scène à exporter.";
            return;
        }

        var path = await _filePickerService.PickSceneExportFileAsync();
        if (path == null) return;
        try
        {
            await _sceneCollectionService.SaveAsync(path, scenes.Select(scene => scene.ToDefinition()).ToArray(), cancellationToken);
            SceneIoStatus = $"{scenes.Count} scène(s) exportée(s).";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SceneIoStatus = $"Export impossible : {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task ImportScenes(CancellationToken cancellationToken)
    {
        var path = await _filePickerService.PickSceneImportFileAsync();
        if (path == null) return;
        try
        {
            var imported = await _sceneCollectionService.LoadAsync(path, cancellationToken);
            if (imported.Count == 0)
            {
                SceneIoStatus = "Aucune scène trouvée dans ce fichier.";
                return;
            }

            var skipped = 0;
            foreach (var definition in imported)
            {
                skipped += definition.Sources.RemoveAll(source => source.Origin is SourceOrigin.HardwareVideo or SourceOrigin.HardwareAudio);
                _workspace.AddScene(definition);
            }
            SceneIoStatus = skipped == 0
                ? $"{imported.Count} scène(s) importée(s)."
                : $"{imported.Count} scène(s) importée(s), {skipped} source(s) matérielle(s) ignorée(s).";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SceneIoStatus = $"Import impossible : {exception.Message}";
        }
    }

    [RelayCommand]
    private void AssignColorToSelection(string color)
    {
        foreach (var scene in GetSelectedScenes()) scene.Color = color;
    }

    [RelayCommand]
    private async Task OpenAddSource()
    {
        if (SelectedScene == null) return;
        var result = await _dialogService.ShowAsync(_dialogFactory.Create(SelectedScene));
        if (result != null) await ApplyAddSourceResultAsync(result);
    }

    public async Task ApplyAddSourceResultAsync(AddSourceResult result)
    {
        if (SelectedScene == null) return;
        switch (result)
        {
            case AddSourceResult.Video video:
                AddHardwareVideo(SelectedScene, video.Option);
                break;
            case AddSourceResult.Audio audio:
                AddHardwareAudio(SelectedScene, audio.Option);
                break;
            case AddSourceResult.Network network:
                AddNetworkSource(SelectedScene, network.Label, network.Url);
                break;
            case AddSourceResult.PickFileVideo:
                await AddFileSourceAsync(SourceKind.Video);
                break;
            case AddSourceResult.PickFileAudio:
                await AddFileSourceAsync(SourceKind.Audio);
                break;
            case AddSourceResult.PickFileMedia:
                await AddFileMediaSourceAsync();
                break;
        }
    }

    [RelayCommand]
    private void RemoveSource(SourceItemViewModel source) => SelectedScene?.Sources.Remove(source);

    private async Task AddFileSourceAsync(SourceKind kind)
    {
        if (SelectedScene == null) return;
        var path = kind == SourceKind.Video
            ? await _filePickerService.PickVideoFileAsync()
            : await _filePickerService.PickAudioFileAsync();
        if (path == null) return;
        AddFileSource(SelectedScene, path, kind);
    }

    private async Task AddFileMediaSourceAsync()
    {
        if (SelectedScene == null) return;
        var path = await _filePickerService.PickVideoFileAsync();
        if (path == null) return;
        AddFileSource(SelectedScene, path, SourceKind.Video);
        AddFileSource(SelectedScene, path, SourceKind.Audio);
    }

    private void AddFileSource(SceneItemViewModel scene, string path, SourceKind kind) =>
        _workspace.AddSource(scene, new SourceDefinition
        {
            Name = Path.GetFileName(path),
            Kind = kind,
            Color = kind == SourceKind.Video ? "#a78bfa" : "#fb923c",
            Origin = SourceOrigin.File,
            OriginLabel = Path.GetFileName(path),
            OriginPath = path,
            Loop = true
        });

    private void AddNetworkSource(SceneItemViewModel scene, string label, string url) =>
        _workspace.AddSource(scene, new SourceDefinition
        {
            Name = label,
            Kind = SourceKind.Video,
            Color = "#5b8def",
            Origin = SourceOrigin.Network,
            OriginLabel = label,
            OriginPath = url
        });

    private void AddHardwareVideo(SceneItemViewModel scene, CaptureSourceOption option) =>
        _workspace.AddSource(scene, new SourceDefinition
        {
            Name = option.Label,
            Kind = SourceKind.Video,
            Color = "#5b8def",
            Origin = SourceOrigin.HardwareVideo,
            OriginLabel = option.Label,
            OriginPath = option.Id
        });

    private void AddHardwareAudio(SceneItemViewModel scene, AudioSourceOption option) =>
        _workspace.AddSource(scene, new SourceDefinition
        {
            Name = option.Label,
            Kind = SourceKind.Audio,
            Color = "#f87171",
            Origin = SourceOrigin.HardwareAudio,
            OriginLabel = option.Label,
            OriginPath = option.Id
        });

    private bool WouldLeaveNoScenesWhileLive(int count) => count >= Scenes.Count && (_workspace.IsRecording || _workspace.IsStreaming);
    private List<SceneItemViewModel> GetSelectedScenes() => Scenes.Where(scene => scene.IsMultiSelected).ToList();
}
