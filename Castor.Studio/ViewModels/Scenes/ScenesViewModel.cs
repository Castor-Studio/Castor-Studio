using System.Collections.ObjectModel;
using System.ComponentModel;
using CastorApplication.Models.Settings;
using CastorApplication.Models.Studio;
using CastorApplication.Services;
using CastorApplication.Services.Dialogs;
using CastorApplication.Services.Studio;
using CastorApplication.Services.Settings;
using CastorApplication.ViewModels.Studio;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.ViewModels.Scenes;

public partial class ScenesViewModel : ViewModelBase
{
    private readonly StudioWorkspaceViewModel _workspace;
    private readonly IStudioRuntime _runtime;
    private readonly IScenePreviewRuntime _previewRuntime;
    private readonly ISceneRuntime _sceneRuntime;
    private readonly ISourceRuntime _sourceRuntime;
    private readonly IFilePickerService _filePickerService;
    private readonly ISceneCollectionService _sceneCollectionService;
    private readonly IAddSourceDialogViewModelFactory _dialogFactory;
    private readonly IAddSourceDialogService _dialogService;
    private readonly SettingsService? _settingsService;
    private readonly VideoCanvasResolutionResolver _resolutionResolver;

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

    public IScenePreviewRuntime PreviewRuntime => _previewRuntime;

    /// <summary>
    /// La composition de la scène sélectionnée, relue dans le moteur. C'est elle qui décide
    /// des cadres que le moteur trace sur l'aperçu de cette page.
    /// </summary>
    public SceneCompositionViewModel Composition { get; }

    [ObservableProperty] private int _baseCanvasWidth = 1920;
    [ObservableProperty] private int _baseCanvasHeight = 1080;

    public string PreviewPlaceholderText => !_previewRuntime.IsAvailable
        ? _previewRuntime.UnavailableMessage
        : SelectedScene == null
            ? "Aucune scène sélectionnée."
            : "";

    public static IReadOnlyList<string> SceneColorPalette { get; } =
    [
        "#5b8def", "#34d399", "#f87171", "#fbbf24", "#a78bfa", "#fb923c", "#8888a0"
    ];

    internal ScenesViewModel(
        StudioWorkspaceViewModel workspace,
        IStudioRuntime runtime,
        IScenePreviewRuntime previewRuntime,
        ISceneRuntime sceneRuntime,
        ISourceRuntime sourceRuntime,
        IFilePickerService filePickerService,
        ISceneCollectionService sceneCollectionService,
        IAddSourceDialogViewModelFactory dialogFactory,
        IAddSourceDialogService dialogService,
        SettingsService? settingsService = null,
        VideoCanvasResolutionResolver? resolutionResolver = null)
    {
        _workspace = workspace;
        _runtime = runtime;
        _previewRuntime = previewRuntime;
        _sceneRuntime = sceneRuntime;
        _sourceRuntime = sourceRuntime;
        _filePickerService = filePickerService;
        _sceneCollectionService = sceneCollectionService;
        _dialogFactory = dialogFactory;
        _dialogService = dialogService;
        _settingsService = settingsService;
        _resolutionResolver = resolutionResolver ?? new VideoCanvasResolutionResolver(settingsService);
        Composition = new SceneCompositionViewModel(sourceRuntime);
        ApplyBaseCanvasResolution(settingsService?.Load() ?? new ApplicationSettings());
        SelectedScene = workspace.ActiveScene;
        workspace.PropertyChanged += OnWorkspacePropertyChanged;
        if (_settingsService != null)
            _settingsService.SettingsSaved += OnSettingsSaved;
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

    partial void OnSelectedSceneChanged(SceneItemViewModel? oldValue, SceneItemViewModel? newValue)
    {
        if (oldValue != null) oldValue.IsSelected = false;
        // Les cadres repartent de la composition que le moteur détient pour cette scène :
        // une scène rouverte se redessine sur ce qui est réellement rendu, pas sur un
        // souvenir.
        Composition.ShowScene(newValue);
        if (newValue != null)
        {
            newValue.IsSelected = true;
            // Afficher une scène, c'est relire son empilement dans le moteur : l'ordre survit
            // ainsi à un rechargement sans qu'on l'ait mémorisé de notre côté.
            SourceOperationStatus = SyncSourceOrder(newValue);
        }

        OnPropertyChanged(nameof(PreviewPlaceholderText));
    }

    private void OnSettingsSaved(object? sender, EventArgs e)
    {
        var settings = _settingsService?.Load();
        if (settings == null) return;

        ApplyBaseCanvasResolution(settings);
    }

    private void ApplyBaseCanvasResolution(ApplicationSettings settings)
    {
        var resolution = _resolutionResolver.Resolve(settings);
        BaseCanvasWidth = resolution.Width;
        BaseCanvasHeight = resolution.Height;
    }

    // The workspace owns the global scene selection. Keep this page's selection projection
    // synchronized with it so Studio, Scenes, recording, and streaming all use one scene.
    [RelayCommand]
    private void SelectScene(SceneItemViewModel scene)
    {
        if (!Scenes.Contains(scene)) return;
        _workspace.SelectScene(scene);
        SelectedScene = _workspace.ActiveScene;
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
        // Select the new scene globally so both pages and any running output stay in sync.
        var scene = _workspace.AddScene(definition);
        SelectScene(scene);
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
        SourceOperationStatus = SyncSourceOrder(scene);
    }

    // ── Empilement des sources (z-order) ─────────────────────────────────────
    //
    // L'ordre appartient au moteur. Chaque geste lui est transmis, puis la liste affichée est
    // réalignée sur ce qu'il renvoie : aucun ordre local n'est entretenu ici, donc aucun ne
    // peut diverger. Rang 0 = premier plan.

    [RelayCommand(CanExecute = nameof(CanRaiseSource))]
    private void RaiseSource(SourceItemViewModel source) => MoveSourceBy(source, -1);

    private bool CanRaiseSource(SourceItemViewModel? source) =>
        source != null && SelectedScene != null && SelectedScene.Sources.IndexOf(source) > 0;

    [RelayCommand(CanExecute = nameof(CanLowerSource))]
    private void LowerSource(SourceItemViewModel source) => MoveSourceBy(source, 1);

    private bool CanLowerSource(SourceItemViewModel? source)
    {
        var scene = SelectedScene;
        if (source == null || scene == null) return false;

        var index = scene.Sources.IndexOf(source);
        return index >= 0 && index < scene.Sources.Count - 1;
    }

    /// <summary>
    /// Lâcher <paramref name="moved"/> sur <paramref name="target"/> lui donne le rang de la
    /// cible. Laisse la vue traiter un drop sans qu'elle ait à calculer un rang elle-même.
    /// </summary>
    internal void MoveSourceHere(SourceItemViewModel moved, SourceItemViewModel target)
    {
        var scene = SelectedScene;
        if (scene == null || ReferenceEquals(moved, target)) return;

        var targetIndex = scene.Sources.IndexOf(target);
        if (targetIndex < 0) return;

        MoveSourceTo(scene, moved, targetIndex);
    }

    private void MoveSourceBy(SourceItemViewModel source, int offset)
    {
        var scene = SelectedScene;
        if (scene == null) return;

        var index = scene.Sources.IndexOf(source);
        if (index < 0) return;

        MoveSourceTo(scene, source, index + offset);
    }

    private void MoveSourceTo(SceneItemViewModel scene, SourceItemViewModel source, int layerIndex)
    {
        if (layerIndex < 0 || layerIndex >= scene.Sources.Count) return;
        if (scene.Sources.IndexOf(source) == layerIndex) return;

        var result = _sourceRuntime.MoveSource(scene.Id, source.Id, layerIndex);
        // Réussite ou refus, on se recale sur le moteur : un refus doit remettre la liste
        // telle qu'elle est réellement rendue, pas telle qu'on l'espérait.
        var syncFailure = SyncSourceOrder(scene);
        SourceOperationStatus = result.IsSuccess ? syncFailure : result.Message;
    }

    /// <summary>
    /// Réaligne la liste affichée sur l'ordre que le moteur détient, et rend son message
    /// d'échec s'il n'a pas pu le donner — sans quoi un déplacement accepté par le moteur
    /// mais illisible ensuite laisserait la liste figée sans rien signaler.
    /// </summary>
    private string SyncSourceOrder(SceneItemViewModel scene)
    {
        var order = _sourceRuntime.GetSourceOrder(scene.Id);
        if (order.IsSuccess)
        {
            // On réordonne seulement : ajouts et suppressions ont leurs propres chemins.
            var target = 0;
            foreach (var sourceId in order.SourceIds)
            {
                var current = IndexOfSource(scene, sourceId);
                if (current < 0) continue;
                if (current != target) scene.Sources.Move(current, target);
                target++;
            }
        }

        // Les extrémités de la pile bougent avec l'ordre, les boutons doivent suivre.
        RaiseSourceCommand.NotifyCanExecuteChanged();
        LowerSourceCommand.NotifyCanExecuteChanged();

        // Tout geste sur les sources passe par ici : la composition se recale dans la
        // foulée, sans attendre la prochaine relecture périodique de l'aperçu.
        Composition.Refresh();

        // Un moteur globalement indisponible est déjà annoncé par l'aperçu ; seul un refus
        // propre à cette scène mérite d'être écrit sous la liste.
        return order.Status == StudioRuntimeStatus.Failure ? order.Message : "";
    }

    private static int IndexOfSource(SceneItemViewModel scene, Guid sourceId)
    {
        for (var index = 0; index < scene.Sources.Count; index++)
        {
            if (scene.Sources[index].Id == sourceId) return index;
        }

        return -1;
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
        // Le moteur empile la nouvelle source au premier plan : on relit plutôt que de
        // supposer où elle a atterri.
        SourceOperationStatus = SyncSourceOrder(scene);
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
