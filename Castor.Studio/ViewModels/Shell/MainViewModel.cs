using System.ComponentModel;
using Avalonia;
using Avalonia.Media;
using CastorApplication.ViewModels.Multicam;
using CastorApplication.ViewModels.Scenes;
using CastorApplication.ViewModels.Settings;
using CastorApplication.ViewModels.Studio;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.ViewModels.Shell;

public enum MainPageKind
{
    Studio,
    Multicam,
    Scenes,
    Settings
}

public partial class MainViewModel : ViewModelBase
{
    private readonly StudioViewModel _studioViewModel;
    private readonly StudioDockViewModel _studioDockViewModel;
    private readonly MulticamViewModel _multicamViewModel;
    private readonly ScenesViewModel _scenesViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly StudioWorkspaceViewModel _workspace;

    public string GlobalStatusText => _workspace.IsStreaming ? "EN DIRECT" : _workspace.IsRecording ? "REC" : "OFFLINE";
    public IBrush GlobalStatusBrush => SolidColorBrush.Parse(_workspace.IsStreaming || _workspace.IsRecording ? "#f87171" : "#3c3c4e");

    [ObservableProperty] private ViewModelBase? _currentPage;
    [ObservableProperty] private MainPageKind _currentPageKind;

    public bool IsStudioActive => CurrentPageKind == MainPageKind.Studio;
    public bool IsMulticamActive => CurrentPageKind == MainPageKind.Multicam;
    public bool IsScenesActive => CurrentPageKind == MainPageKind.Scenes;
    public bool IsSettingsActive => CurrentPageKind == MainPageKind.Settings;

    internal MainViewModel(
        StudioViewModel studioViewModel,
        StudioDockViewModel studioDockViewModel,
        MulticamViewModel multicamViewModel,
        ScenesViewModel scenesViewModel,
        SettingsViewModel settingsViewModel,
        StudioWorkspaceViewModel workspace)
    {
        _studioViewModel = studioViewModel;
        _studioDockViewModel = studioDockViewModel;
        _multicamViewModel = multicamViewModel;
        _scenesViewModel = scenesViewModel;
        _settingsViewModel = settingsViewModel;
        _workspace = workspace;
        _workspace.PropertyChanged += OnWorkspacePropertyChanged;
        ShowStudio();
    }

    [RelayCommand]
    private void ShowStudio()
    {
        _studioViewModel.RefreshOutputInfo();
        CurrentPage = _studioDockViewModel;
        CurrentPageKind = MainPageKind.Studio;
    }

    [RelayCommand]
    private void ShowMulticam()
    {
        CurrentPage = _multicamViewModel;
        CurrentPageKind = MainPageKind.Multicam;
    }

    [RelayCommand]
    private void ShowScenes()
    {
        CurrentPage = _scenesViewModel;
        CurrentPageKind = MainPageKind.Scenes;
    }

    [RelayCommand]
    private void ShowSettings()
    {
        CurrentPage = _settingsViewModel;
        CurrentPageKind = MainPageKind.Settings;
    }

    public void ApplyScreenSize(Size screenSize) => _studioDockViewModel.ApplyScreenSize(screenSize);

    public void PresentFloatingPanels() => _studioDockViewModel.PresentFloatingPanels();

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(StudioWorkspaceViewModel.IsRecording) or nameof(StudioWorkspaceViewModel.IsStreaming))) return;
        OnPropertyChanged(nameof(GlobalStatusText));
        OnPropertyChanged(nameof(GlobalStatusBrush));
    }

    partial void OnCurrentPageKindChanged(MainPageKind value)
    {
        OnPropertyChanged(nameof(IsStudioActive));
        OnPropertyChanged(nameof(IsMulticamActive));
        OnPropertyChanged(nameof(IsScenesActive));
        OnPropertyChanged(nameof(IsSettingsActive));
    }
}
