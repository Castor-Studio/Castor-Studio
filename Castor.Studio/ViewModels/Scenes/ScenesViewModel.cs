using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using CastorApplication.Models.Export;
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
    private readonly ISceneTransferDialogService _transferDialogService;
    private readonly SettingsService? _settingsService;
    private readonly VideoCanvasResolutionResolver _resolutionResolver;
    private readonly string? _savedScenesPath;
    // The scenes as last read from or written to _savedScenesPath, in the file's own format:
    // anything the file would not keep (selection, live state) is not a change worth saving.
    private string _savedScenesSnapshot;

    public ObservableCollection<SceneItemViewModel> Scenes => _workspace.Scenes;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenAddSourceCommand))]
    [NotifyPropertyChangedFor(nameof(AddSourceToolTip))]
    private SceneItemViewModel? _selectedScene;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateSceneCommand))]
    private string _newSceneName = "";
    [ObservableProperty] private string _createSceneError = "";
    [ObservableProperty] private bool _isSelectionModeActive;
    [ObservableProperty] private string _deleteSceneError = "";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmRenameSceneCommand))]
    private SceneItemViewModel? _sceneBeingRenamed;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmRenameSceneCommand))]
    private string _renameSceneName = "";
    [ObservableProperty] private string _renameSceneError = "";
    [ObservableProperty] private SceneItemViewModel? _sceneBeingColored;
    // Import/export outcome: a confirmation fades, a failure stays until dismissed, and either
    // goes away when another scene is selected or the page changes.
    public StatusMessage SceneIoStatus { get; }
    [ObservableProperty] private string _sourceOperationStatus = "";

    // Sources as listed on screen: SelectedScene.Sources sorted and filtered for reading. The
    // scene's collection itself is never reordered, so the composition order stays intact.
    [ObservableProperty] private IReadOnlyList<SourceItemViewModel> _displayedSources = [];
    [ObservableProperty] private SourceListSort _sourceSort = SourceListSort.SceneOrder;
    [ObservableProperty] private SourceListFilter _sourceFilter = SourceListFilter.All;
    private SceneItemViewModel? _observedSourcesScene;
    [ObservableProperty] private SourceItemViewModel? _sourceBeingRenamed;
    [ObservableProperty] private string _renameSourceName = "";

    public IReadOnlyList<SourceListOption> SourceSortOptions { get; } =
    [
        SourceListOption.ForSort("Ordre de la scène", SourceListSort.SceneOrder),
        SourceListOption.ForSort("Nom (A → Z)", SourceListSort.NameAscending),
        SourceListOption.ForSort("Nom (Z → A)", SourceListSort.NameDescending),
        SourceListOption.ForSort("Type", SourceListSort.Kind)
    ];

    public IReadOnlyList<SourceListOption> SourceFilterOptions { get; } =
    [
        SourceListOption.ForFilter("Toutes", SourceListFilter.All),
        SourceListOption.ForFilter("Vidéo", SourceListFilter.Video),
        SourceListOption.ForFilter("Audio", SourceListFilter.Audio),
        SourceListOption.ForFilter("Média", SourceListFilter.Media)
    ];

    public bool IsSourceListCustomized => SourceSort != SourceListSort.SceneOrder || SourceFilter != SourceListFilter.All;

    public string SourceListSummary
    {
        get
        {
            if (SelectedScene == null) return "";
            var total = SelectedScene.Sources.Count;
            return SourceFilter == SourceListFilter.All
                ? $"{total}"
                : $"{DisplayedSources.Count} sur {total}";
        }
    }

    // Without a scene there is nowhere to put a source: the list says what to do first, and
    // the source + stays disabled with the same reason on hover.
    public string SourceListPlaceholder => SelectedScene == null
        ? Scenes.Count == 0
            ? "Sélectionnez ou créez une scène pour ajouter des sources."
            : "Sélectionnez une scène pour voir ses sources."
        : DisplayedSources.Count > 0
            ? ""
            : SelectedScene.Sources.Count == 0
                ? "Aucune source dans cette scène. Utilisez « Ajouter une source » pour commencer."
                : "Aucune source de ce type.";

    public string AddSourceToolTip => SelectedScene == null
        ? "Créez ou sélectionnez d'abord une scène"
        : "Ajouter une source";

    public IScenePreviewRuntime PreviewRuntime => _previewRuntime;

    /// <summary>
    /// The selected scene's composition, read from the engine. It decides the frames the engine
    /// draws on this page's preview.
    /// </summary>
    public SceneCompositionViewModel Composition { get; }

    [ObservableProperty] private int _baseCanvasWidth = 1920;
    [ObservableProperty] private int _baseCanvasHeight = 1080;

    public string PreviewPlaceholderText => !_previewRuntime.IsAvailable
        ? _previewRuntime.UnavailableMessage
        : SelectedScene == null
            ? "Aucune scène sélectionnée."
            : "";

    // What the canvas area shows. An unavailable engine is not an empty screen:
    // the preview itself says what is wrong.
    public bool ShowNoScene => _previewRuntime.IsAvailable && SelectedScene == null;
    public bool ShowNoVideoSource => _previewRuntime.IsAvailable && SelectedScene != null
        && !StudioWorkspaceViewModel.HasVideoSource(SelectedScene);
    public bool ShowScenePreview => !ShowNoScene && !ShowNoVideoSource;

    /// <summary>The canvas size and its reduced proportions: 1920 × 1080, 16:9.</summary>
    public string CanvasRatioText
    {
        get
        {
            var divisor = (int)System.Numerics.BigInteger.GreatestCommonDivisor(BaseCanvasWidth, BaseCanvasHeight);
            return divisor == 0 ? "" : $"{BaseCanvasWidth / divisor}:{BaseCanvasHeight / divisor}";
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewScaleText))]
    private bool _isPreviewActualSize;

    public string PreviewScaleText => IsPreviewActualSize ? "Taille réelle" : "Ajusté à la fenêtre";

    [RelayCommand]
    private void FitPreview() => IsPreviewActualSize = false;

    [RelayCommand]
    private void ShowPreviewActualSize() => IsPreviewActualSize = true;

    partial void OnBaseCanvasWidthChanged(int value) => OnPropertyChanged(nameof(CanvasRatioText));

    partial void OnBaseCanvasHeightChanged(int value) => OnPropertyChanged(nameof(CanvasRatioText));

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
        ISceneTransferDialogService transferDialogService,
        SettingsService? settingsService = null,
        VideoCanvasResolutionResolver? resolutionResolver = null,
        StatusMessageService? messages = null,
        string? savedScenesPath = null)
    {
        SceneIoStatus = (messages ?? new StatusMessageService()).Create();
        _savedScenesPath = savedScenesPath;
        _transferDialogService = transferDialogService;
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
        Composition = new SceneCompositionViewModel(sourceRuntime, arrange: ArrangeSource);
        ApplyBaseCanvasResolution(settingsService?.Load() ?? new ApplicationSettings());
        SelectedScene = workspace.ActiveScene;
        RefreshDisplayedSources();
        _savedScenesSnapshot = TakeScenesSnapshot();
        workspace.PropertyChanged += OnWorkspacePropertyChanged;
        if (_settingsService != null)
            _settingsService.SettingsSaved += OnSettingsSaved;
    }

    public bool HasUnsavedSceneChanges => _savedScenesPath != null && TakeScenesSnapshot() != _savedScenesSnapshot;

    private string TakeScenesSnapshot() =>
        JsonSerializer.Serialize(Scenes.Select(scene => SceneExportMapper.ToExport(scene.ToDefinition())));

    // Startup: the scenes saved at the end of the previous session come back. No file yet is
    // a first launch, not a failure.
    public async Task LoadSavedScenesAsync(CancellationToken cancellationToken = default)
    {
        if (_savedScenesPath == null) return;

        try
        {
            var saved = await _sceneCollectionService.LoadAsync(_savedScenesPath, cancellationToken);
            ImportDefinitions(saved, announceSuccess: false);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SceneIoStatus.ShowError($"Scènes enregistrées illisibles : {exception.Message}");
        }

        _savedScenesSnapshot = TakeScenesSnapshot();
    }

    // Every scene, in their current order. False when the file could not be written: the error
    // is on the Scenes page and nothing is considered saved.
    public async Task<bool> SaveScenesAsync(CancellationToken cancellationToken = default)
    {
        if (_savedScenesPath == null) return true;

        try
        {
            var snapshot = TakeScenesSnapshot();
            await _sceneCollectionService.SaveAsync(
                _savedScenesPath, Scenes.Select(scene => scene.ToDefinition()).ToArray(), cancellationToken);
            _savedScenesSnapshot = snapshot;
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SceneIoStatus.ShowError($"Enregistrement des scènes impossible : {exception.Message}");
            return false;
        }
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
        SceneIoStatus.Clear();
        // Frames start again from the composition the engine holds for this scene: a reopened scene
        // redraws from what is really rendered, not from memory.
        Composition.ShowScene(newValue);
        if (newValue != null)
        {
            newValue.IsSelected = true;
            // Showing a scene means rereading its stack from the engine: order thus survives a reload
            // without being stored on our side.
            SourceOperationStatus = SyncSourceOrder(newValue);
        }

        OnPropertyChanged(nameof(PreviewPlaceholderText));
        ObserveSources(newValue);
    }

    // Follows the selected scene's sources so the displayed list stays current when a source
    // is added, removed or moved in the scene.
    private void ObserveSources(SceneItemViewModel? scene)
    {
        EndSourceRename();
        if (_observedSourcesScene != null)
            _observedSourcesScene.Sources.CollectionChanged -= OnSelectedSceneSourcesChanged;
        _observedSourcesScene = scene;
        if (scene != null)
            scene.Sources.CollectionChanged += OnSelectedSceneSourcesChanged;
        RefreshDisplayedSources();
    }

    private void OnSelectedSceneSourcesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (SourceBeingRenamed != null && _observedSourcesScene?.Sources.Contains(SourceBeingRenamed) != true)
            EndSourceRename();
        RefreshDisplayedSources();
    }

    partial void OnSourceSortChanged(SourceListSort value) => RefreshDisplayedSources();

    partial void OnSourceFilterChanged(SourceListFilter value) => RefreshDisplayedSources();

    private void RefreshDisplayedSources()
    {
        DisplayedSources = SelectedScene == null
            ? []
            : SourceListView.Apply(SelectedScene.Sources, SourceSort, SourceFilter);
        foreach (var option in SourceSortOptions) option.IsSelected = option.Sort == SourceSort;
        foreach (var option in SourceFilterOptions) option.IsSelected = option.Filter == SourceFilter;
        OnPropertyChanged(nameof(IsSourceListCustomized));
        OnPropertyChanged(nameof(SourceListSummary));
        OnPropertyChanged(nameof(SourceListPlaceholder));
        OnPropertyChanged(nameof(ShowNoScene));
        OnPropertyChanged(nameof(ShowNoVideoSource));
        OnPropertyChanged(nameof(ShowScenePreview));
    }

    [RelayCommand]
    private void ApplySourceListOption(SourceListOption option)
    {
        if (option.Sort is { } sort) SourceSort = sort;
        if (option.Filter is { } filter) SourceFilter = filter;
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

    // Typing again clears the previous failure; the flyout reads a non-empty error as "keep open".
    partial void OnNewSceneNameChanged(string value) => CreateSceneError = "";

    private bool CanCreateScene() => !string.IsNullOrWhiteSpace(NewSceneName);

    [RelayCommand(CanExecute = nameof(CanCreateScene))]
    private void CreateScene()
    {
        if (!CanCreateScene()) return;

        var definition = new SceneDefinition { Name = NewSceneName.Trim() };
        var result = _sceneRuntime.CreateScene(definition.Id, definition.Name);
        if (!result.IsSuccess)
        {
            CreateSceneError = result.Message;
            return;
        }

        definition.Name = result.EffectiveName;
        // Select the new scene globally so both pages and any running output stay in sync.
        var scene = _workspace.AddScene(definition);
        SelectScene(scene);
        NewSceneName = "";
    }

    private const string RecordedSceneError =
        "Cette scène est en cours d'enregistrement. Arrêtez son enregistrement avant de la supprimer.";

    [RelayCommand]
    private void DeleteScene(SceneItemViewModel scene)
    {
        if (scene.IsBeingRecorded)
        {
            DeleteSceneError = RecordedSceneError;
            return;
        }

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
            if (scene.IsBeingRecorded)
            {
                failures.Add($"{scene.Name} : {RecordedSceneError}");
                continue;
            }

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
    private void StartSelectedFileScenesTogether() => SceneIoStatus.ShowError(_runtime.UnavailableMessage);

    [RelayCommand]
    private void BeginRenameScene(SceneItemViewModel scene)
    {
        SceneBeingRenamed = scene;
        RenameSceneName = scene.Name;
        RenameSceneError = "";
    }

    private bool CanConfirmRenameScene() =>
        SceneBeingRenamed != null && !string.IsNullOrWhiteSpace(RenameSceneName);

    [RelayCommand(CanExecute = nameof(CanConfirmRenameScene))]
    private void ConfirmRenameScene()
    {
        if (SceneBeingRenamed == null || string.IsNullOrWhiteSpace(RenameSceneName)) return;

        var result = _sceneRuntime.RenameScene(SceneBeingRenamed.Id, RenameSceneName);
        if (!result.IsSuccess)
        {
            RenameSceneError = result.Message;
            return;
        }

        SceneBeingRenamed.Name = result.EffectiveName;
        SceneBeingRenamed = null;
        RenameSceneError = "";
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

    // The scenes are chosen first, in a dialog that pre-checks the current selection (every scene
    // when nothing is selected) and counts what will be written; the file comes after. Cancelling
    // either step writes nothing.
    [RelayCommand]
    private async Task ExportScenes(CancellationToken cancellationToken)
    {
        if (Scenes.Count == 0)
        {
            SceneIoStatus.ShowInfo("Aucune scène à exporter.");
            return;
        }

        var dialog = SceneTransferDialogViewModel.ForExport(Scenes);
        if (!await _transferDialogService.ShowAsync(dialog)) return;

        var scenes = dialog.CheckedItems.Select(item => item.Definition).ToArray();
        if (scenes.Length == 0) return;

        var path = await _filePickerService.PickSceneExportFileAsync();
        if (path == null) return;
        try
        {
            await _sceneCollectionService.SaveAsync(path, scenes, cancellationToken);
            SceneIoStatus.ShowInfo($"{scenes.Length} scène(s) exportée(s).");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SceneIoStatus.ShowError($"Export impossible : {exception.Message}");
        }
    }

    // One or more files, then a dialog listing every scene they hold, all checked, with name and
    // identity collisions flagged. Only what is still checked when the dialog is confirmed is
    // written; cancelling it writes nothing.
    [RelayCommand]
    private async Task ImportScenes(CancellationToken cancellationToken)
    {
        var paths = await _filePickerService.PickSceneImportFilesAsync();
        if (paths.Count == 0) return;

        var found = new List<SceneTransferItem>();
        var fileErrors = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                var scenes = await _sceneCollectionService.LoadAsync(path, cancellationToken);
                var origin = paths.Count > 1 ? Path.GetFileName(path) : "";
                found.AddRange(scenes.Select(scene => new SceneTransferItem(scene, origin)));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                fileErrors.Add($"{Path.GetFileName(path)} : {exception.Message}");
            }
        }

        if (found.Count == 0)
        {
            if (fileErrors.Count == 0) SceneIoStatus.ShowInfo("Aucune scène trouvée dans ce fichier.");
            else SceneIoStatus.ShowError($"Import impossible : {string.Join(" | ", fileErrors)}");
            return;
        }

        var dialog = SceneTransferDialogViewModel.ForImport(Scenes, found, fileErrors);
        if (!await _transferDialogService.ShowAsync(dialog)) return;

        // The same scene twice (already in the project, or twice in the import) comes in as a copy:
        // the engine refuses a second scene with an identity it already holds.
        var takenIds = Scenes.Select(scene => scene.Id).ToHashSet();
        var imported = new List<SceneDefinition>();
        foreach (var item in dialog.CheckedItems)
        {
            var definition = item.Definition;
            if (!takenIds.Add(definition.Id))
            {
                definition.Id = Guid.NewGuid();
                takenIds.Add(definition.Id);
            }
            imported.Add(definition);
        }

        ImportDefinitions(imported);
    }

    private void ImportDefinitions(IReadOnlyList<SceneDefinition> imported, bool announceSuccess = true)
    {
        try
        {
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
                    var sourceResult = _sourceRuntime.AddSource(definition.Id, CreateImportRequest(source));
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
            if (sourceFailures > 0) details.Add($"{sourceFailures} source(s) refusée(s) ({firstSourceFailure})");
            // Anything left behind has to be read before it goes away: it stays like an error.
            // Restoring the saved scenes at startup only speaks up when something was left behind.
            if (details.Count > 0)
                SceneIoStatus.ShowError($"{importedCount} scène(s) importée(s), {string.Join(", ", details)}.");
            else if (announceSuccess)
                SceneIoStatus.ShowInfo($"{importedCount} scène(s) importée(s).");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SceneIoStatus.ShowError($"Import impossible : {exception.Message}");
        }
    }

    [RelayCommand]
    private void AssignColorToSelection(string color)
    {
        foreach (var scene in GetSelectedScenes()) scene.Color = color;
    }

    private bool HasSelectedScene() => SelectedScene != null;

    [RelayCommand(CanExecute = nameof(HasSelectedScene))]
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
            case AddSourceResult.Network network:
                AddNetworkCamera(SelectedScene, network.Name, network.Url);
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

    // ── Source stacking (z-order) ─────────────────────────────────────────────
    //
    // Order belongs to the engine. Each gesture is sent to it, then the displayed list is
    // realigned on what it returns: no local order is kept here, so none can diverge.
    // Rank 0 = foreground.

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
    /// Dropping <paramref name="moved"/> on <paramref name="target"/> gives it the target's rank.
    /// Lets the view handle a drop without computing a rank itself.
    /// </summary>
    internal void MoveSourceHere(SourceItemViewModel moved, SourceItemViewModel target)
    {
        var scene = SelectedScene;
        if (scene == null || ReferenceEquals(moved, target)) return;

        var targetIndex = scene.Sources.IndexOf(target);
        if (targetIndex < 0) return;

        MoveSourceTo(scene, moved, targetIndex);
    }

    // The canvas asks for a layer for its selected source: it only knows the source's id, so
    // the stacking is resolved here.
    private void ArrangeSource(Guid sourceId, SourceArrangement arrangement)
    {
        var scene = SelectedScene;
        if (scene == null) return;

        var index = IndexOfSource(scene, sourceId);
        if (index < 0) return;

        MoveSourceTo(scene, scene.Sources[index], arrangement switch
        {
            SourceArrangement.Forward => index - 1,
            SourceArrangement.Backward => index + 1,
            SourceArrangement.ToFront => 0,
            _ => scene.Sources.Count - 1
        });
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
        // Success or refusal, realign on the engine: a refusal must put the list back as it is really
        // rendered, not as hoped.
        var syncFailure = SyncSourceOrder(scene);
        SourceOperationStatus = result.IsSuccess ? syncFailure : result.Message;
    }

    /// <summary>
    /// Realigns the displayed list on the order the engine holds, and returns its failure message
    /// if it could not give it. Otherwise a move the engine accepted but cannot read back would
    /// leave the list frozen with no report.
    /// </summary>
    private string SyncSourceOrder(SceneItemViewModel scene)
    {
        var order = _sourceRuntime.GetSourceOrder(scene.Id);
        if (order.IsSuccess)
        {
            // Reordering only: additions and removals have their own paths.
            var target = 0;
            foreach (var sourceId in order.SourceIds)
            {
                var current = IndexOfSource(scene, sourceId);
                if (current < 0) continue;
                if (current != target) scene.Sources.Move(current, target);
                target++;
            }
        }

        // The ends of the stack move with the order, the buttons must follow.
        RaiseSourceCommand.NotifyCanExecuteChanged();
        LowerSourceCommand.NotifyCanExecuteChanged();

        // Every gesture on sources goes through here: the composition realigns right away, without
        // waiting for the preview's next periodic read.
        Composition.Refresh();

        // A globally unavailable engine is already announced by the preview; only a refusal specific
        // to this scene deserves to be written under the list.
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
    private void BeginRenameSource(SourceItemViewModel source)
    {
        if (SelectedScene?.Sources.Contains(source) != true) return;
        if (SourceBeingRenamed != null) SourceBeingRenamed.IsRenaming = false;

        RenameSourceName = source.Name;
        SourceBeingRenamed = source;
        source.IsRenaming = true;
    }

    // Enter and leaving the field both confirm. An empty or unchanged name just ends the
    // edit; a refused one keeps the field open with the reason under the list.
    [RelayCommand]
    private void ConfirmRenameSource()
    {
        var source = SourceBeingRenamed;
        var scene = SelectedScene;
        if (source == null) return;

        var requested = RenameSourceName.Trim();
        if (scene == null || requested.Length == 0 || requested == source.Name)
        {
            EndSourceRename();
            return;
        }

        var result = _sourceRuntime.RenameSource(scene.Id, source.Id, requested);
        if (!result.IsSuccess)
        {
            SourceOperationStatus = result.Message;
            return;
        }

        source.Name = string.IsNullOrWhiteSpace(result.EffectiveName) ? requested : result.EffectiveName;
        SourceOperationStatus = "";
        EndSourceRename();
        // A name sort has to place the renamed source again.
        RefreshDisplayedSources();
    }

    [RelayCommand]
    private void CancelRenameSource() => EndSourceRename();

    private void EndSourceRename()
    {
        if (SourceBeingRenamed != null) SourceBeingRenamed.IsRenaming = false;
        SourceBeingRenamed = null;
        RenameSourceName = "";
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
            OriginPath = option.Id,
            VideoCaptureKind = option.Type
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
            OriginPath = option.Id,
            AudioCaptureKind = option.Type
        };
        AddSource(scene, definition, new SourceAddRequest.Audio(definition.Id, definition.Name, option));
    }

    // The stream key stays in OriginPath only: the label shows the server alone.
    private void AddNetworkCamera(SceneItemViewModel scene, string name, string url)
    {
        var definition = new SourceDefinition
        {
            Name = name,
            Kind = SourceKind.Video,
            Color = "#5b8def",
            Origin = SourceOrigin.Network,
            OriginLabel = RtmpStreamUrl.Describe(url),
            OriginPath = url,
            VideoCaptureKind = Models.Studio.VideoCaptureKind.Network
        };
        AddSource(scene, definition, new SourceAddRequest.Network(definition.Id, definition.Name, url));
    }

    private static SourceAddRequest CreateImportRequest(SourceDefinition source) => source.Origin == SourceOrigin.Network
        ? new SourceAddRequest.Network(source.Id, source.Name, source.OriginPath)
        : new SourceAddRequest.Media(source.Id, source.Name, source.OriginPath, source.Loop);

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
        // The engine stacks the new source in the foreground: read back rather than assume where it
        // landed.
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
            // Older collections may hold rtsp:// or http:// streams: only RTMP can be played back.
            if (source.Origin == SourceOrigin.Network && RtmpStreamUrl.IsValid(source.OriginPath))
            {
                source.Kind = SourceKind.Video;
                source.VideoCaptureKind = Models.Studio.VideoCaptureKind.Network;
                normalized.Add(source);
                continue;
            }

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
