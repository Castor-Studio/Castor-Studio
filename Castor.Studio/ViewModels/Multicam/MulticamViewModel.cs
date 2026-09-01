using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CastorApplication.Services.Ai;
using CastorApplication.ViewModels.Scenes;
using CastorApplication.ViewModels.Studio;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.ViewModels.Multicam;

public sealed partial class AiSceneSelection : ViewModelBase
{
    public SceneItemViewModel Scene { get; }
    public string Name => Scene.Name;
    public int SourceCount => Scene.Sources.Count;

    [ObservableProperty] private bool _isSelected;

    public AiSceneSelection(SceneItemViewModel scene)
    {
        Scene = scene;
        Scene.PropertyChanged += OnScenePropertyChanged;
        Scene.Sources.CollectionChanged += OnSourcesChanged;
    }

    private void OnScenePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SceneItemViewModel.Name)) OnPropertyChanged(nameof(Name));
    }

    private void OnSourcesChanged(object? sender, NotifyCollectionChangedEventArgs e) => OnPropertyChanged(nameof(SourceCount));
}

public partial class MulticamViewModel : ViewModelBase
{
    private readonly IAiAnalysisClient _aiAnalysisClient;
    private readonly StudioWorkspaceViewModel _workspace;

    public ObservableCollection<SceneItemViewModel> Scenes => _workspace.Scenes;
    public ObservableCollection<AiSceneSelection> AiScenes { get; } = [];

    [ObservableProperty] private bool _isAiOff = true;
    [ObservableProperty] private bool _isAiAgent;
    [ObservableProperty] private bool _isAiAuto;
    [ObservableProperty] private int _selectedAiModelIndex;
    [ObservableProperty] private string _aiStatusText = "IA désactivée";
    [ObservableProperty] private string _aiError = "";
    [ObservableProperty] private bool _isAiBusy;

    public bool IsAiEnabled => !IsAiOff;

    internal MulticamViewModel(IAiAnalysisClient aiAnalysisClient, StudioWorkspaceViewModel workspace)
    {
        _aiAnalysisClient = aiAnalysisClient;
        _workspace = workspace;
        RefreshAiScenes();
        Scenes.CollectionChanged += (_, _) => RefreshAiScenes();
    }

    [RelayCommand]
    private void RefreshAiScenes()
    {
        var selectedIds = AiScenes.Where(item => item.IsSelected).Select(item => item.Scene.Id).ToHashSet();
        AiScenes.Clear();
        foreach (var scene in Scenes)
            AiScenes.Add(new AiSceneSelection(scene) { IsSelected = selectedIds.Contains(scene.Id) });
    }

    [RelayCommand]
    private void SetAiOff()
    {
        IsAiOff = true;
        IsAiAgent = false;
        IsAiAuto = false;
        AiError = "";
        AiStatusText = "IA désactivée";
        OnPropertyChanged(nameof(IsAiEnabled));
    }

    [RelayCommand]
    private void SetAiAgent() => ShowUnavailable();

    [RelayCommand]
    private void SetAiAuto() => ShowUnavailable();

    private void ShowUnavailable()
    {
        IsAiOff = true;
        IsAiAgent = false;
        IsAiAuto = false;
        AiStatusText = "IA indisponible";
        AiError = _aiAnalysisClient.UnavailableMessage;
        OnPropertyChanged(nameof(IsAiEnabled));
    }
}
