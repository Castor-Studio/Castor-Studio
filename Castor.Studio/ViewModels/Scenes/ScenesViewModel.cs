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
    private readonly ISceneRuntime _sceneRuntime;
    private readonly ISourceRuntime _sourceRuntime;
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
    [ObservableProperty] private string _sourceOperationStatus = "";

    public static IReadOnlyList<string> SceneColorPalette { get; } =
    [
        "#5b8def", "#34d399", "#f87171", "#fbbf24", "#a78bfa", "#fb923c", "#8888a0"
    ];

    internal ScenesViewModel(
        StudioWorkspaceViewModel workspace,
        IStudioRuntime runtime,
        ISceneRuntime sceneRuntime,
        ISourceRuntime sourceRuntime,
        IFilePickerService filePickerService,
        ISceneCollectionService sceneCollectionService,
        IAddSourceDialogViewModelFactory dialogFactory,
        IAddSourceDialogService dialogService)
    {
        _workspace = workspace;
        _runtime = runtime;
        _sceneRuntime = sceneRuntime;
        _sourceRuntime = sourceRuntime;
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

        var definition = new SceneDefinition { Name = NewSceneName.Trim() };
        var result = _sceneRuntime.CreateScene(definition.Id, definition.Name);
        if (!result.IsSuccess)
        {
            SceneIoStatus = result.Message;
            return;
        }

        definition.Name = result.EffectiveName;
        SelectedScene = _workspace.AddScene(definition);
        NewSceneName = "";
        SceneIoStatus = "";
    }

    [RelayCommand]
    private void DeleteScene(SceneItemViewModel scene)
    {
        if (WouldLeaveNoScenesWhileLive(1))
        {
            DeleteSceneError = "Impossible de supprimer la seule scène pendant un enregistrement ou un live.";
            return;
        }

        var result = _sceneRuntime.RemoveScene(scene.Id);
        if (!result.IsSuccess)
        {
            DeleteSceneError = result.Message;
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

        var failures = new List<string>();
        foreach (var scene in selected)
        {
            var result = _sceneRuntime.RemoveScene(scene.Id);
            if (!result.IsSuccess)
            {
                failures.Add($"{scene.Name} : {result.Message}");
                continue;
            }

            _workspace.DeleteScene(scene);
        }

        DeleteSceneError = failures.Count == 0
            ? ""
            : $"{failures.Count} scène(s) non supprimée(s) : {string.Join(" | ", failures)}";
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

        var result = _sceneRuntime.RenameScene(SceneBeingRenamed.Id, RenameSceneName);
        if (!result.IsSuccess)
        {
            SceneIoStatus = result.Message;
            return;
        }

        SceneBeingRenamed.Name = result.EffectiveName;
        SceneBeingRenamed = null;
        SceneIoStatus = "";
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
            var failed = 0;
            var sourceFailures = 0;
            var importedCount = 0;
            var firstFailure = "";
            var firstSourceFailure = "";
            foreach (var definition in imported)
            {
                var importedSources = NormalizeImportedSources(definition.Sources, ref skipped);
                definition.Sources = [];

                var result = _sceneRuntime.CreateScene(definition.Id, definition.Name);
                if (!result.IsSuccess)
                {
                    failed++;
                    if (firstFailure.Length == 0) firstFailure = result.Message;
                    continue;
                }

                definition.Name = result.EffectiveName;
                foreach (var source in importedSources)
                {
                    var sourceResult = _sourceRuntime.AddSource(definition.Id, new SourceAddRequest.Media(
                        source.Id, source.Name, source.OriginPath, source.Loop));
                    if (!sourceResult.IsSuccess)
                    {
                        sourceFailures++;
                        if (firstSourceFailure.Length == 0) firstSourceFailure = sourceResult.Message;
                        continue;
                    }

                    source.Name = sourceResult.EffectiveName;
                    definition.Sources.Add(source);
                }
                _workspace.AddScene(definition);
                importedCount++;
            }

            var details = new List<string>();
            if (skipped > 0) details.Add($"{skipped} source(s) non prise(s) en charge ignorée(s)");
            if (failed > 0) details.Add($"{failed} scène(s) refusée(s) ({firstFailure})");
            if (sourceFailures > 0) details.Add($"{sourceFailures} source(s) média refusée(s) ({firstSourceFailure})");
            SceneIoStatus = details.Count == 0
                ? $"{importedCount} scène(s) importée(s)."
                : $"{importedCount} scène(s) importée(s), {string.Join(", ", details)}.";
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
            case AddSourceResult.Media:
                await AddFileMediaSourceAsync();
                break;
        }
    }

    [RelayCommand]
    private void RemoveSource(SourceItemViewModel source)
    {
        var scene = SelectedScene;
        if (scene == null) return;

        var result = _sourceRuntime.RemoveSource(scene.Id, source.Id);
        if (!result.IsSuccess)
        {
            SourceOperationStatus = result.Message;
            return;
        }

        scene.Sources.Remove(source);
        SourceOperationStatus = "";
    }

    [RelayCommand]
    private void ToggleMediaLoop(SourceItemViewModel source)
    {
        var scene = SelectedScene;
        if (scene == null || !source.IsFileSource) return;

        var loop = !source.Loop;
        var result = _sourceRuntime.SetMediaLoop(scene.Id, source.Id, loop);
        if (!result.IsSuccess)
        {
            SourceOperationStatus = result.Message;
            source.RefreshLoopState();
            return;
        }

        source.Loop = loop;
        SourceOperationStatus = "";
    }

    private async Task AddFileMediaSourceAsync()
    {
        if (SelectedScene == null) return;
        var path = await _filePickerService.PickMediaFileAsync();
        if (path == null) return;

        var definition = new SourceDefinition
        {
            Name = Path.GetFileName(path),
            Kind = SourceKind.Media,
            Color = "#a78bfa",
            Origin = SourceOrigin.File,
            OriginLabel = Path.GetFileName(path),
            OriginPath = path,
            Loop = true
        };
        AddSource(SelectedScene, definition,
            new SourceAddRequest.Media(definition.Id, definition.Name, path, definition.Loop));
    }

    private void AddHardwareVideo(SceneItemViewModel scene, CaptureSourceOption option)
    {
        var definition = new SourceDefinition
        {
            Name = option.Label,
            Kind = SourceKind.Video,
            Color = "#5b8def",
            Origin = SourceOrigin.HardwareVideo,
            OriginLabel = option.Label,
            OriginPath = option.Id
        };
        AddSource(scene, definition, new SourceAddRequest.Video(definition.Id, definition.Name, option));
    }

    private void AddHardwareAudio(SceneItemViewModel scene, AudioSourceOption option)
    {
        var definition = new SourceDefinition
        {
            Name = option.Label,
            Kind = SourceKind.Audio,
            Color = "#f87171",
            Origin = SourceOrigin.HardwareAudio,
            OriginLabel = option.Label,
            OriginPath = option.Id
        };
        AddSource(scene, definition, new SourceAddRequest.Audio(definition.Id, definition.Name, option));
    }

    private void AddSource(SceneItemViewModel scene, SourceDefinition definition, SourceAddRequest request)
    {
        var result = _sourceRuntime.AddSource(scene.Id, request);
        if (!result.IsSuccess)
        {
            SourceOperationStatus = result.Message;
            return;
        }

        definition.Name = result.EffectiveName;
        _workspace.AddSource(scene, definition);
        SourceOperationStatus = "";
    }

    private static List<SourceDefinition> NormalizeImportedSources(
        IEnumerable<SourceDefinition> sources,
        ref int skipped)
    {
        var sourceList = sources.ToList();
        var pairedLegacyPaths = sourceList
            .Where(source => source.Origin == SourceOrigin.File &&
                             source.Kind is SourceKind.Video or SourceKind.Audio &&
                             !string.IsNullOrWhiteSpace(source.OriginPath))
            .GroupBy(source => source.OriginPath, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Any(source => source.Kind == SourceKind.Video) &&
                            group.Any(source => source.Kind == SourceKind.Audio))
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mergedLegacyPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<SourceDefinition>();

        foreach (var source in sourceList)
        {
            if (source.Origin != SourceOrigin.File)
            {
                skipped++;
                continue;
            }

            if (pairedLegacyPaths.Contains(source.OriginPath) && !mergedLegacyPaths.Add(source.OriginPath))
                continue;

            source.Kind = SourceKind.Media;
            normalized.Add(source);
        }

        return normalized;
    }

    private bool WouldLeaveNoScenesWhileLive(int count) => count >= Scenes.Count && (_workspace.IsRecording || _workspace.IsStreaming);
    private List<SceneItemViewModel> GetSelectedScenes() => Scenes.Where(scene => scene.IsMultiSelected).ToList();
}
