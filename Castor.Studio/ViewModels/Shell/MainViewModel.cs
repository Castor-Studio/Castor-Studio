using System.ComponentModel;
using Avalonia.Media;
using CastorApplication.Docking;
using CastorApplication.ViewModels.Settings;
using CastorApplication.ViewModels.Studio;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.ViewModels.Shell;

public enum MainPageKind
{
    StudioWorkspace,
    Settings
}

public partial class MainViewModel : ViewModelBase
{
    private readonly StudioViewModel _studioViewModel;
    private readonly StudioDockViewModel _studioDockViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly StudioWorkspaceViewModel _workspace;

    public string GlobalStatusText => _workspace.IsStreaming ? "EN DIRECT" : _workspace.IsRecording ? "REC" : "OFFLINE";
    public IBrush GlobalStatusBrush => SolidColorBrush.Parse(_workspace.IsStreaming || _workspace.IsRecording ? "#f87171" : "#3c3c4e");

    [ObservableProperty] private ViewModelBase? _currentPage;
    [ObservableProperty] private MainPageKind _currentPageKind;

    public bool IsStudioActive => CurrentPageKind == MainPageKind.StudioWorkspace && _studioDockViewModel.FocusedPaneId == StudioDockIds.Preview;
    public bool IsMulticamActive => CurrentPageKind == MainPageKind.StudioWorkspace && _studioDockViewModel.FocusedPaneId == StudioDockIds.Multicam;
    public bool IsScenesActive => CurrentPageKind == MainPageKind.StudioWorkspace && _studioDockViewModel.FocusedPaneId == StudioDockIds.Scenes;
    public bool IsSettingsActive => CurrentPageKind == MainPageKind.Settings;

    internal MainViewModel(
        StudioViewModel studioViewModel,
        StudioDockViewModel studioDockViewModel,
        SettingsViewModel settingsViewModel,
        StudioWorkspaceViewModel workspace)
    {
        _studioViewModel = studioViewModel;
        _studioDockViewModel = studioDockViewModel;
        _settingsViewModel = settingsViewModel;
        _workspace = workspace;
        _workspace.PropertyChanged += OnWorkspacePropertyChanged;
        _studioDockViewModel.PropertyChanged += OnStudioDockPropertyChanged;
        ShowStudio();
    }

    [RelayCommand]
    private void ShowStudio()
    {
        _studioViewModel.RefreshOutputInfo();
        CurrentPage = _studioDockViewModel;
        CurrentPageKind = MainPageKind.StudioWorkspace;
        _studioDockViewModel.FocusPane(StudioDockIds.Preview);
    }

    [RelayCommand]
    private void ShowMulticam()
    {
        CurrentPage = _studioDockViewModel;
        CurrentPageKind = MainPageKind.StudioWorkspace;
        _studioDockViewModel.FocusPane(StudioDockIds.Multicam);
    }

    [RelayCommand]
    private void ShowScenes()
    {
        CurrentPage = _studioDockViewModel;
        CurrentPageKind = MainPageKind.StudioWorkspace;
        _studioDockViewModel.FocusPane(StudioDockIds.Scenes);
    }

    [RelayCommand]
    private void ShowSettings()
    {
        CurrentPage = _settingsViewModel;
        CurrentPageKind = MainPageKind.Settings;
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(StudioWorkspaceViewModel.IsRecording) or nameof(StudioWorkspaceViewModel.IsStreaming))) return;
        OnPropertyChanged(nameof(GlobalStatusText));
        OnPropertyChanged(nameof(GlobalStatusBrush));
    }

    private void OnStudioDockPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(StudioDockViewModel.FocusedPaneId)) return;
        OnPropertyChanged(nameof(IsStudioActive));
        OnPropertyChanged(nameof(IsMulticamActive));
        OnPropertyChanged(nameof(IsScenesActive));
    }

    partial void OnCurrentPageKindChanged(MainPageKind value)
    {
        OnPropertyChanged(nameof(IsStudioActive));
        OnPropertyChanged(nameof(IsMulticamActive));
        OnPropertyChanged(nameof(IsScenesActive));
        OnPropertyChanged(nameof(IsSettingsActive));
    }
}
