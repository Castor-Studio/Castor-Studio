using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CastorApplication.ViewModels.Settings;

namespace CastorApplication.ViewModels;

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
    private readonly MulticamViewModel _multicamViewModel;
    private readonly ScenesViewModel _scenesViewModel;
    private readonly SettingsViewModel _settingsViewModel;

    [ObservableProperty]
    private ViewModelBase? _currentPage;

    [ObservableProperty]
    private MainPageKind _currentPageKind;

    public bool IsStudioActive => CurrentPageKind == MainPageKind.Studio;
    public bool IsMulticamActive => CurrentPageKind == MainPageKind.Multicam;
    public bool IsScenesActive => CurrentPageKind == MainPageKind.Scenes;
    public bool IsSettingsActive => CurrentPageKind == MainPageKind.Settings;

    public MainViewModel(
        StudioViewModel studioViewModel,
        MulticamViewModel multicamViewModel,
        ScenesViewModel scenesViewModel,
        SettingsViewModel settingsViewModel)
    {
        _studioViewModel = studioViewModel;
        _multicamViewModel = multicamViewModel;
        _scenesViewModel = scenesViewModel;
        _settingsViewModel = settingsViewModel;

        ShowStudio();
    }

    [RelayCommand]
    private void ShowStudio()
    {
        CurrentPage = _studioViewModel;
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

    partial void OnCurrentPageKindChanged(MainPageKind value)
    {
        OnPropertyChanged(nameof(IsStudioActive));
        OnPropertyChanged(nameof(IsMulticamActive));
        OnPropertyChanged(nameof(IsScenesActive));
        OnPropertyChanged(nameof(IsSettingsActive));
    }
}
