using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Threading;
using Castor.AI.Providers;
using CastorApplication.Models.Settings;
using CastorApplication.Models.Studio;
using CastorApplication.Services.Ai;
using CastorApplication.Services.Settings;
using CastorApplication.Services.Studio;
using CastorApplication.ViewModels.Scenes;
using CastorApplication.ViewModels.Studio;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CastorApplication.Docking;
using Dock.Model.Controls;

namespace CastorApplication.ViewModels.Multicam;

/// <summary>
/// One cell of the multicam grid: a scene, rendered live, that can also be
/// ticked for the AI to reason about.
/// </summary>
public sealed partial class MulticamSceneTile : ViewModelBase
{
    private readonly StudioWorkspaceViewModel _workspace;
    private readonly Action<MulticamSceneTile>? _selectionChanged;
    private readonly Action<MulticamSceneTile>? _aiSetupChanged;
    private readonly Action? _sourcesChanged;

    public SceneItemViewModel Scene { get; }
    public string Name => Scene.Name;
    public int SourceCount => Scene.Sources.Count;

    public string SourceCountText => SourceCount switch
    {
        0 => "Aucune source",
        1 => "1 source",
        var count => $"{count} sources",
    };

    // Each tile draws on its own native surface. LibObsSceneRuntime keeps one
    // preview session per window, so every tile renders at the same time
    // instead of taking turns - which is the whole point of a grid.
    public IScenePreviewRuntime PreviewRuntime { get; }

    [ObservableProperty] private bool _isSelected;

    // Set by the AI pipeline through MulticamViewModel.SetAiFocus, never by the tile
    // itself: one scene at a time may be Selected, which only the owner of the
    // whole collection can guarantee.
    [ObservableProperty] private MulticamAiState _aiState = MulticamAiState.None;
    [ObservableProperty] private int _baseCanvasWidth = 1920;
    [ObservableProperty] private int _baseCanvasHeight = 1080;

    /// <summary>Whether this is the scene currently going to the output.</summary>
    public bool IsOnAir => ReferenceEquals(_workspace.ActiveScene, Scene);

    /// <summary>Whether this scene is actually leaving the app: streamed, recorded, or both.</summary>
    public bool IsOutputLive => IsOnAir && MulticamViewModel.IsOutputRunning(_workspace);

    /// <summary>What the tile's tally says, empty unless this is the output scene.</summary>
    public string OutputLabel => IsOnAir ? MulticamViewModel.OutputStateText(_workspace) : "";

    /// <summary>Whether the scene has anything to show. Drives the empty state.</summary>
    public bool HasVideo => StudioWorkspaceViewModel.HasVideoSource(Scene);

    public bool IsAiSelected => AiState == MulticamAiState.Selected;
    public bool IsAiConsidered => AiState == MulticamAiState.Considered;

    // What the AI is told about this scene. Kept on the tile like its AI tick, so that it
    // survives the grid being rebuilt.

    /// <summary>Every audio source of the workspace, shared by all tiles.</summary>
    public IReadOnlyList<MulticamMicrophoneOption> AiMicrophoneOptions { get; }

    public IReadOnlyList<MulticamAiRoleOption> AiRoleOptions => MulticamAiRoleOption.Podcast;

    [ObservableProperty] private MulticamMicrophoneOption _aiMicrophone = MulticamMicrophoneOption.None;
    [ObservableProperty] private MulticamAiRoleOption _aiRole = MulticamAiRoleOption.Podcast[0];

    // Roles only mean something to the podcast worker.
    [ObservableProperty] private bool _isPodcastModule;

    public bool ShowAiMicrophone => IsSelected;
    public bool ShowAiRole => IsSelected && IsPodcastModule;
    public bool ShowAiSetup => ShowAiMicrophone || ShowAiRole;

    public string PreviewPlaceholderText => !PreviewRuntime.IsAvailable
        ? PreviewRuntime.UnavailableMessage
        : !StudioWorkspaceViewModel.HasVideoSource(Scene)
            ? "Pas de source vidéo"
            : "";

    internal MulticamSceneTile(SceneItemViewModel scene, StudioWorkspaceViewModel workspace,
        IScenePreviewRuntime previewRuntime, int baseCanvasWidth, int baseCanvasHeight,
        Action<MulticamSceneTile>? selectionChanged = null,
        IReadOnlyList<MulticamMicrophoneOption>? microphoneOptions = null,
        Action<MulticamSceneTile>? aiSetupChanged = null,
        Action? sourcesChanged = null)
    {
        Scene = scene;
        _workspace = workspace;
        PreviewRuntime = previewRuntime;
        BaseCanvasWidth = baseCanvasWidth;
        BaseCanvasHeight = baseCanvasHeight;
        _selectionChanged = selectionChanged;
        AiMicrophoneOptions = microphoneOptions ?? [MulticamMicrophoneOption.None];
        _aiSetupChanged = aiSetupChanged;
        _sourcesChanged = sourcesChanged;
        Scene.PropertyChanged += OnScenePropertyChanged;
        Scene.Sources.CollectionChanged += OnSourcesChanged;
    }

    internal void NotifyOnAirChanged()
    {
        OnPropertyChanged(nameof(IsOnAir));
        NotifyOutputStateChanged();
    }

    internal void NotifyOutputStateChanged()
    {
        OnPropertyChanged(nameof(IsOutputLive));
        OnPropertyChanged(nameof(OutputLabel));
    }

    // Lets the tile view act on a drop without holding a reference back to the page.
    internal void MoveSceneHere(SceneItemViewModel moved) => _workspace.MoveScene(moved, Scene);

    // Deliberate, explicit action - unlike selecting a scene on the Scenes page, which
    // must never move what is being broadcast.
    [RelayCommand]
    private void PutOnAir() => _workspace.SelectScene(Scene);

    partial void OnAiStateChanged(MulticamAiState value)
    {
        OnPropertyChanged(nameof(IsAiSelected));
        OnPropertyChanged(nameof(IsAiConsidered));
    }

    partial void OnIsSelectedChanged(bool value)
    {
        NotifyAiSetupVisibility();
        _selectionChanged?.Invoke(this);
    }

    partial void OnIsPodcastModuleChanged(bool value) => NotifyAiSetupVisibility();

    // A ComboBox briefly clears its selection while its items are rebuilt: never keep a null.
    partial void OnAiMicrophoneChanged(MulticamMicrophoneOption value)
    {
        if (value is null)
        {
            AiMicrophone = MulticamMicrophoneOption.None;
            return;
        }

        _aiSetupChanged?.Invoke(this);
    }

    partial void OnAiRoleChanged(MulticamAiRoleOption value)
    {
        if (value is null)
        {
            AiRole = MulticamAiRoleOption.Podcast[0];
            return;
        }

        _aiSetupChanged?.Invoke(this);
    }

    private void NotifyAiSetupVisibility()
    {
        OnPropertyChanged(nameof(ShowAiMicrophone));
        OnPropertyChanged(nameof(ShowAiRole));
        OnPropertyChanged(nameof(ShowAiSetup));
    }

    internal void ApplyBaseCanvas(int width, int height)
    {
        BaseCanvasWidth = width;
        BaseCanvasHeight = height;
    }

    private void OnScenePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SceneItemViewModel.Name)) OnPropertyChanged(nameof(Name));
    }

    private void OnSourcesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(SourceCount));
        OnPropertyChanged(nameof(SourceCountText));
        OnPropertyChanged(nameof(HasVideo));
        OnPropertyChanged(nameof(PreviewPlaceholderText));
        _sourcesChanged?.Invoke();
    }
}

public partial class MulticamViewModel : ViewModelBase
{
    private readonly IAiAnalysisClient _aiAnalysisClient;
    private readonly StudioWorkspaceViewModel _workspace;
    private readonly IScenePreviewRuntime _previewRuntime;
    private readonly SettingsService? _settingsService;

    // Kept so a tile rebuilt after a scene change picks its marking back up.
    private SceneItemViewModel? _aiSelectedScene;
    private IReadOnlySet<SceneItemViewModel> _aiConsideredScenes = new HashSet<SceneItemViewModel>();

    public ObservableCollection<SceneItemViewModel> Scenes => _workspace.Scenes;

    /// <summary>The grid itself: one live tile per scene in the workspace.</summary>
    public ObservableCollection<MulticamSceneTile> Tiles { get; } = [];

    /// <summary>Audio sources of every scene, which any AI scene can use as its microphone.</summary>
    public ObservableCollection<MulticamMicrophoneOption> AiMicrophoneOptions { get; } = [MulticamMicrophoneOption.None];

    public bool HasScenes => Tiles.Count > 0;

    // The empty wall leads to the Scenes page, where scenes are made (MainViewModel).
    public event EventHandler? ScenesPageRequested;

    [RelayCommand]
    private void OpenScenes() => ScenesPageRequested?.Invoke(this, EventArgs.Empty);

    // A multiview fills the page rather than flowing cards: the column count follows
    // the scene count so every tile stays as large as it can be.
    public int GridColumns => ColumnChoice > 0
        ? ColumnChoice
        : Tiles.Count <= 1 ? 1 : Tiles.Count <= 4 ? 2 : Tiles.Count <= 9 ? 3 : 4;

    // 0 means the column count follows the scene count; anything else pins it.
    [ObservableProperty] private int _columnChoice;
    [ObservableProperty] private MulticamLayout _layout = MulticamLayout.Grid;

    private readonly MulticamDockFactory _dockFactory = new();

    /// <summary>The dock tree, only built while the docked layout is showing.</summary>
    [ObservableProperty] private IRootDock? _dockLayout;

    public bool IsGridLayout => Layout == MulticamLayout.Grid;
    public bool IsSpotlightLayout => Layout == MulticamLayout.Spotlight;

    // The spotlight renders the scene on air on a surface of its own rather than
    // borrowing a tile: a preview per window is exactly what the runtime supports,
    // and it saves keeping a second, filtered collection of tiles in step.
    public SceneItemViewModel? SpotlightScene => _workspace.ActiveScene;

    // The tally: green while the output scene is only chosen, red once it actually leaves
    // the app. Same words as the top bar, so the two never disagree.
    internal static bool IsOutputRunning(StudioWorkspaceViewModel workspace) =>
        workspace.IsStreaming || workspace.IsRecording;

    internal static string OutputStateText(StudioWorkspaceViewModel workspace)
    {
        var recording = workspace.RecordingState == RecordingState.Paused ? "PAUSE" : "REC";
        return (workspace.IsStreaming, workspace.IsRecording) switch
        {
            (true, true) => $"En direct + {recording}",
            (true, false) => "En direct",
            (false, true) => recording,
            _ => "Prête",
        };
    }

    public bool IsOutputLive => SpotlightScene != null && IsOutputRunning(_workspace);

    public string OutputLabel => SpotlightScene != null ? OutputStateText(_workspace) : "";

    public IScenePreviewRuntime PreviewRuntime => _previewRuntime;

    [ObservableProperty] private int _baseCanvasWidth = 1920;
    [ObservableProperty] private int _baseCanvasHeight = 1080;

    public string SpotlightPlaceholderText => !_previewRuntime.IsAvailable
        ? _previewRuntime.UnavailableMessage
        : SpotlightScene == null
            ? "Aucune scène en sortie."
            : !StudioWorkspaceViewModel.HasVideoSource(SpotlightScene)
                ? "Pas de source vidéo"
                : "";

    [ObservableProperty] private bool _isAiOff = true;
    [ObservableProperty] private bool _isAiAgent;
    [ObservableProperty] private bool _isAiAuto;
    [ObservableProperty] private int _selectedAiModelIndex;
    [ObservableProperty] private string _aiStatusText = "IA désactivée";
    [ObservableProperty] private string _aiError = "";
    [ObservableProperty] private bool _isAiBusy;
    [ObservableProperty] private MulticamAiSessionState _aiSessionState = MulticamAiSessionState.Off;
    [ObservableProperty] private string _aiSuggestionName = "";
    [ObservableProperty] private string _aiSuggestionConfidence = "";
    private Guid? _aiSuggestionSceneId;
    private bool _aiSelectionDirty;

    public bool IsAiEnabled => !IsAiOff;
    public bool HasAiSuggestion => _aiSuggestionSceneId.HasValue && IsAiAgent;

    internal MulticamViewModel(
        IAiAnalysisClient aiAnalysisClient,
        StudioWorkspaceViewModel workspace,
        IScenePreviewRuntime? previewRuntime = null,
        SettingsService? settingsService = null)
    {
        _aiAnalysisClient = aiAnalysisClient;
        _workspace = workspace;
        _previewRuntime = previewRuntime ?? new UnavailableScenePreviewRuntime();
        _settingsService = settingsService;
        (BaseCanvasWidth, BaseCanvasHeight) = CurrentBaseCanvas();
        _aiAnalysisClient.SceneSwitchSuggested += OnSceneSwitchSuggested;
        _aiAnalysisClient.SceneSwitchRequested += OnSceneSwitchRequested;
        _aiAnalysisClient.SessionStatusChanged += OnSessionStatusChanged;
        _aiAnalysisClient.ServerErrorReceived += OnServerErrorReceived;
        RefreshTiles();
        Scenes.CollectionChanged += OnScenesChanged;
        _workspace.PropertyChanged += OnWorkspacePropertyChanged;
        if (_settingsService != null)
            _settingsService.SettingsSaved += OnSettingsSaved;

        // Built up front rather than when the mode is picked: DockControl reads its
        // layout as it attaches, and one handed over later never reaches it.
        RebuildDockLayout();
    }

    [RelayCommand]
    private void RefreshTiles()
    {
        var (width, height) = CurrentBaseCanvas();

        // Reconciled in place rather than rebuilt: clearing the collection would
        // tear down and restart every native preview on the page each time one
        // scene is added, removed or moved. It also means a tile keeps whatever
        // the operator had set on it - its AI tick above all.
        for (var index = Tiles.Count - 1; index >= 0; index--)
        {
            if (!Scenes.Contains(Tiles[index].Scene)) Tiles.RemoveAt(index);
        }

        for (var index = 0; index < Scenes.Count; index++)
        {
            var scene = Scenes[index];
            var existing = Tiles.FirstOrDefault(tile => tile.Scene == scene);

            if (existing == null)
            {
                Tiles.Insert(index, new MulticamSceneTile(scene, _workspace, _previewRuntime, width, height,
                    OnTileSelectionChanged, AiMicrophoneOptions, OnTileAiSetupChanged, RefreshMicrophoneOptions)
                {
                    IsPodcastModule = IsPodcastModule,
                });
                continue;
            }

            var current = Tiles.IndexOf(existing);
            if (current != index) Tiles.Move(current, index);
        }

        // A scene that has left the workspace can no longer be the AI's pick.
        if (_aiSelectedScene != null && !Scenes.Contains(_aiSelectedScene)) _aiSelectedScene = null;
        ApplyAiFocus();
        RefreshMicrophoneOptions();

        OnPropertyChanged(nameof(HasScenes));
        RebuildDockLayout();
        OnPropertyChanged(nameof(GridColumns));
    }

    private (int Width, int Height) CurrentBaseCanvas()
    {
        var resolution = VideoResolution.BaseFromIndex(_settingsService?.Load().SelectedBaseResolutionIndex ?? 1);
        return (resolution.Width, resolution.Height);
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StudioWorkspaceViewModel.IsStreaming)
            or nameof(StudioWorkspaceViewModel.RecordingState))
        {
            foreach (var tile in Tiles) tile.NotifyOutputStateChanged();
            OnPropertyChanged(nameof(IsOutputLive));
            OnPropertyChanged(nameof(OutputLabel));
            return;
        }

        if (e.PropertyName != nameof(StudioWorkspaceViewModel.ActiveScene)) return;

        // Every cut, from this page or any other: the AI reasons about what is on air. Its own
        // cuts in auto mode come back here too and are ignored by the client.
        if (IsAiEnabled && _workspace.ActiveScene is { } onAir) _aiAnalysisClient.ReportOnAir(onAir.Id);

        foreach (var tile in Tiles) tile.NotifyOnAirChanged();
        OnPropertyChanged(nameof(SpotlightScene));
        OnPropertyChanged(nameof(SpotlightPlaceholderText));
        OnPropertyChanged(nameof(IsOutputLive));
        OnPropertyChanged(nameof(OutputLabel));
    }

    private void OnScenesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshTiles();
        if (IsAiBusy)
        {
            _aiSelectionDirty = true;
            return;
        }
        if (IsAiEnabled) _ = SynchronizeSelectedScenesAsync();
    }

    // Reconciled in place, like the tiles: an open ComboBox keeps its items.
    private void RefreshMicrophoneOptions()
    {
        List<MulticamMicrophoneOption> wanted =
        [
            MulticamMicrophoneOption.None,
            .. Scenes.SelectMany(scene => scene.Sources
                .Where(source => source.Kind == SourceKind.Audio)
                .Select(source => new MulticamMicrophoneOption(scene.Id, source.Id, $"{source.Name} ({scene.Name})"))),
        ];

        for (var index = AiMicrophoneOptions.Count - 1; index >= 0; index--)
        {
            if (!wanted.Contains(AiMicrophoneOptions[index])) AiMicrophoneOptions.RemoveAt(index);
        }

        for (var index = 0; index < wanted.Count; index++)
        {
            if (index < AiMicrophoneOptions.Count && AiMicrophoneOptions[index] == wanted[index]) continue;
            var current = AiMicrophoneOptions.IndexOf(wanted[index]);
            if (current >= 0) AiMicrophoneOptions.Move(current, index);
            else AiMicrophoneOptions.Insert(index, wanted[index]);
        }

        // A microphone removed from its scene can no longer be listened to.
        foreach (var tile in Tiles)
        {
            if (!AiMicrophoneOptions.Contains(tile.AiMicrophone)) tile.AiMicrophone = MulticamMicrophoneOption.None;
        }
    }

    private bool IsPodcastModule => GetSelectedModuleName() == "podcast";

    partial void OnSelectedAiModelIndexChanged(int value)
    {
        foreach (var tile in Tiles) tile.IsPodcastModule = IsPodcastModule;
    }

    // A new microphone or role only matters to the AI for a scene it reads.
    private void OnTileAiSetupChanged(MulticamSceneTile tile)
    {
        if (tile.IsSelected) OnTileSelectionChanged(tile);
    }

    private void OnTileSelectionChanged(MulticamSceneTile tile)
    {
        if (IsAiBusy)
        {
            _aiSelectionDirty = true;
            return;
        }
        if (IsAiEnabled) _ = SynchronizeSelectedScenesAsync();
    }

    partial void OnIsAiBusyChanged(bool value)
    {
        if (value || !_aiSelectionDirty || !IsAiEnabled) return;
        _aiSelectionDirty = false;
        _ = SynchronizeSelectedScenesAsync();
    }

    private void OnSettingsSaved(object? sender, EventArgs e)
    {
        var (width, height) = CurrentBaseCanvas();
        BaseCanvasWidth = width;
        BaseCanvasHeight = height;
        foreach (var tile in Tiles) tile.ApplyBaseCanvas(width, height);
    }

    /// <summary>
    /// Records what the AI is doing with the scenes, as one atomic picture.
    /// Anything not named is cleared, so the grid can never keep showing a
    /// decision the pipeline has moved on from.
    /// </summary>
    /// <param name="selected">The scene the AI would switch to, if any.</param>
    /// <param name="considered">Candidates it is weighing. The selected scene need not repeat here.</param>
    public void SetAiFocus(SceneItemViewModel? selected, IEnumerable<SceneItemViewModel>? considered = null)
    {
        _aiSelectedScene = selected;
        _aiConsideredScenes = considered?.ToHashSet() ?? [];
        ApplyAiFocus();
    }

    // Also runs after the tiles are rebuilt, so a scene added or removed elsewhere
    // does not silently erase what the pipeline last reported.
    private void ApplyAiFocus()
    {
        foreach (var tile in Tiles)
        {
            tile.AiState = ReferenceEquals(tile.Scene, _aiSelectedScene)
                ? MulticamAiState.Selected
                : _aiConsideredScenes.Contains(tile.Scene)
                    ? MulticamAiState.Considered
                    : MulticamAiState.None;
        }
    }

    /// <summary>Drops every AI marking, for when the pipeline stops or is turned off.</summary>
    public void ClearAiFocus() => SetAiFocus(null);

    partial void OnColumnChoiceChanged(int value) => OnPropertyChanged(nameof(GridColumns));

    partial void OnLayoutChanged(MulticamLayout value)
    {
        OnPropertyChanged(nameof(IsGridLayout));
        OnPropertyChanged(nameof(IsSpotlightLayout));
        RebuildDockLayout();
    }

    /// <summary>
    /// Puts one scene where another sits, reordering the workspace itself so the
    /// order holds on every page rather than only on this one.
    /// </summary>
    public void MoveScene(SceneItemViewModel moved, SceneItemViewModel target) =>
        _workspace.MoveScene(moved, target);

    // Rebuilt from the scenes rather than persisted: there is no saved arrangement to
    // reconcile against a scene set that changed since, which is what makes a dynamic
    // dock layout expensive elsewhere.
    private void RebuildDockLayout()
    {
        var layout = _dockFactory.CreateDisplayLayout(this);
        _dockFactory.InitLayout(layout);
        DockLayout = layout;
    }

    [RelayCommand]
    private void UseGridLayout() => Layout = MulticamLayout.Grid;

    [RelayCommand]
    private void UseSpotlightLayout() => Layout = MulticamLayout.Spotlight;

    [RelayCommand]
    private void SetColumns(int columns) => ColumnChoice = columns;

    [RelayCommand]
    private async Task SetAiOff() => await StopAiAsync(false, "user_disabled");

    private async Task StopAiAsync(bool force, string reason)
    {
        if (IsAiBusy && !force) return;
        IsAiBusy = true;
        try { await _aiAnalysisClient.StopSessionAsync(reason, CancellationToken.None); }
        catch (Exception exception) { AiError = exception.Message; }
        finally
        {
            ClearAiSessionState();
            IsAiBusy = false;
        }
    }

    [RelayCommand]
    private async Task SetAiAgent() => await StartAiAsync("agent");

    [RelayCommand]
    private async Task SetAiAuto() => await StartAiAsync("auto");

    [RelayCommand]
    private void ApplyAiSuggestion()
    {
        if (!IsAiAgent || _aiSuggestionSceneId is not { } sceneId) return;
        var scene = Scenes.FirstOrDefault(candidate => candidate.Id == sceneId);
        if (scene == null || !_aiAnalysisClient.ActiveSceneIds.Contains(sceneId)) return;

        _workspace.SelectScene(scene);
        AiStatusText = $"Scène appliquée : {scene.Name} ({AiSuggestionConfidence})";
        ClearAiSuggestion();
    }

    private async Task StartAiAsync(string mode)
    {
        if (IsAiBusy) return;
        var selectedScenes = SelectedAiScenes();
        if (selectedScenes.Count == 0)
        {
            AiError = "Sélectionnez au moins une scène avec une source vidéo.";
            AiStatusText = "IA désactivée";
            return;
        }

        IsAiBusy = true;
        AiError = "";
        AiSessionState = MulticamAiSessionState.Connecting;
        AiStatusText = "Démarrage de l'IA...";
        try
        {
            await _aiAnalysisClient.StopSessionAsync("mode_switch", CancellationToken.None);
            await _aiAnalysisClient.StartSessionAsync(
                GetSelectedModuleName(), mode,
                selectedScenes,
                _workspace.ActiveScene?.Id,
                CancellationToken.None);

            IsAiOff = false;
            IsAiAgent = mode == "agent";
            IsAiAuto = mode == "auto";
            AiSessionState = MulticamAiSessionState.Active;
            OnPropertyChanged(nameof(IsAiEnabled));
            OnPropertyChanged(nameof(HasAiSuggestion));
            AiStatusText = $"IA active - {selectedScenes.Count} scène(s)";
        }
        catch (Exception exception)
        {
            try { await _aiAnalysisClient.StopSessionAsync("start_failed", CancellationToken.None); }
            catch { }
            ClearAiSessionState();
            AiSessionState = MulticamAiSessionState.Error;
            AiStatusText = "Erreur IA";
            AiError = exception.Message;
        }
        finally { IsAiBusy = false; }
    }

    private async Task SynchronizeSelectedScenesAsync()
    {
        var selectedScenes = SelectedAiScenes();
        if (selectedScenes.Count == 0)
        {
            await StopAiAsync(true, "no_sources");
            return;
        }

        IsAiBusy = true;
        AiError = "";
        AiSessionState = MulticamAiSessionState.StartingStreams;
        AiStatusText = "Redémarrage de l'IA avec les nouvelles scènes...";
        try
        {
            await _aiAnalysisClient.UpdateSourcesAsync(
                selectedScenes,
                _workspace.ActiveScene?.Id,
                CancellationToken.None);
            // The AI starts again from scratch: a pick made on the old scene set is gone.
            ClearAiSuggestion();
            AiSessionState = MulticamAiSessionState.Active;
            AiStatusText = $"IA active - {selectedScenes.Count} scène(s)";
        }
        catch (Exception exception)
        {
            // A failed restart leaves no session behind.
            ClearAiSessionState();
            AiSessionState = MulticamAiSessionState.Error;
            AiStatusText = "Erreur IA";
            AiError = exception.Message;
        }
        finally { IsAiBusy = false; }
    }

    private List<AiSceneSetup> SelectedAiScenes() => Tiles
        .Where(tile => tile.IsSelected && StudioWorkspaceViewModel.HasVideoSource(tile.Scene))
        .Select(tile => new AiSceneSetup(
            tile.Scene.ToDefinition(),
            IsPodcastModule ? tile.AiRole.Value : "",
            tile.AiMicrophone is { SceneId: { } sceneId, SourceId: { } sourceId }
                ? new AiMicrophone(sceneId, sourceId)
                : null))
        .ToList();

    private void ClearAiSessionState()
    {
        _aiSelectionDirty = false;
        IsAiOff = true;
        IsAiAgent = false;
        IsAiAuto = false;
        AiStatusText = "IA désactivée";
        AiSessionState = MulticamAiSessionState.Off;
        ClearAiSuggestion();
        OnPropertyChanged(nameof(IsAiEnabled));
        OnPropertyChanged(nameof(HasAiSuggestion));
    }

    private void ClearAiSuggestion()
    {
        _aiSuggestionSceneId = null;
        AiSuggestionName = "";
        AiSuggestionConfidence = "";
        SetAiFocus(null);
        OnPropertyChanged(nameof(HasAiSuggestion));
    }

    // Agent mode: shown on the page, the operator decides.
    private void OnSceneSwitchSuggested(object? sender, AiSceneSwitchEvent aiEvent)
    {
        if (!_aiAnalysisClient.ActiveSceneIds.Contains(aiEvent.SceneId)) return;

        void Apply()
        {
            if (!IsAiAgent) return;
            var scene = Scenes.FirstOrDefault(candidate => candidate.Id == aiEvent.SceneId);
            if (scene == null) return;
            _aiSuggestionSceneId = scene.Id;
            AiSuggestionName = scene.Name;
            AiSuggestionConfidence = $"{aiEvent.Confidence:P0}";
            SetAiFocus(scene);
            OnPropertyChanged(nameof(HasAiSuggestion));
            AiStatusText = $"Suggestion IA : {scene.Name} ({AiSuggestionConfidence})";
        }

        RunOnUiThread(Apply);
    }

    // Auto mode: the AI already took the scene, the output follows.
    private void OnSceneSwitchRequested(object? sender, AiSceneSwitchEvent aiEvent)
    {
        if (!_aiAnalysisClient.ActiveSceneIds.Contains(aiEvent.SceneId)) return;

        void Apply()
        {
            if (!IsAiAuto) return;
            var scene = Scenes.FirstOrDefault(candidate => candidate.Id == aiEvent.SceneId);
            if (scene == null) return;
            _workspace.SelectScene(scene);
            AiStatusText = $"Scène IA : {scene.Name} ({aiEvent.Confidence:P0})";
            SetAiFocus(scene);
        }

        RunOnUiThread(Apply);
    }

    private void OnSessionStatusChanged(object? sender, AiSessionStatusEvent aiEvent)
    {
        void Apply()
        {
            switch (aiEvent.State)
            {
                // Model download progress shows here ("downloading football/... 42 %").
                case AiConnectionState.Starting:
                    AiStatusText = $"Démarrage de l'IA : {aiEvent.Message}";
                    break;
                case AiConnectionState.Restarting:
                    if (!IsAiEnabled) return;
                    AiSessionState = MulticamAiSessionState.Connecting;
                    AiStatusText = $"Le worker IA redémarre : {aiEvent.Message}";
                    break;
                case AiConnectionState.Running:
                    if (!IsAiEnabled) return;
                    AiSessionState = MulticamAiSessionState.Active;
                    AiError = "";
                    AiStatusText = $"IA active - {_aiAnalysisClient.ActiveSceneIds.Count} scène(s)";
                    break;
                case AiConnectionState.Faulted:
                    if (IsAiEnabled) AiSessionState = MulticamAiSessionState.Error;
                    break;
            }
        }

        RunOnUiThread(Apply);
    }

    private void OnServerErrorReceived(object? sender, AiServerErrorEvent aiEvent)
    {
        void Apply()
        {
            AiError = aiEvent.ErrorMessage;
            if (aiEvent.IsFatal)
            {
                AiStatusText = "Erreur IA fatale";
                _ = StopAiAfterFatalAsync();
            }
        }

        RunOnUiThread(Apply);
    }

    private static void RunOnUiThread(Action action)
    {
        if (Application.Current == null || Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    private async Task StopAiAfterFatalAsync()
    {
        await StopAiAsync(true, "fatal_server_error");
        AiSessionState = MulticamAiSessionState.Error;
        AiStatusText = "Erreur IA fatale";
    }

    private string GetSelectedModuleName() => SelectedAiModelIndex switch
    {
        1 => "podcast",
        _ => "football"
    };
}
