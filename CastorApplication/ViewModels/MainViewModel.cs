using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CastorApplication.ViewModels.Settings;

namespace CastorApplication.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly StudioViewModel _studioViewModel;
    private readonly MulticamViewModel _multicamViewModel;
    private readonly ScenesViewModel _scenesViewModel;
    private readonly SettingsViewModel _settingsViewModel;

    [ObservableProperty]
    private ViewModelBase? _currentPage;

    [ObservableProperty]
    private bool _isStudioActive = true;

    [ObservableProperty]
    private bool _isMulticamActive = false;

    [ObservableProperty]
    private bool _isScenesActive = false;

    [ObservableProperty]
    private bool _isSettingsActive = false;

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

        ResetTabs();
        ShowStudio();
    }

    [RelayCommand]
    private void ShowStudio()
    {
        CurrentPage = _studioViewModel;
        ResetTabs();
        IsStudioActive = true;
    }

    [RelayCommand]
    private void ShowMulticam()
    {
        CurrentPage = _multicamViewModel;
        ResetTabs();
        IsMulticamActive = true;
    }

    [RelayCommand]
    private void ShowScenes()
    {
        CurrentPage = _scenesViewModel;
        ResetTabs();
        IsScenesActive = true;
    }

    [RelayCommand]
    private void ShowSettings()
    {
        CurrentPage = _settingsViewModel;
        ResetTabs();
        IsSettingsActive = true;
    }

    private void ResetTabs()
    {
        IsStudioActive = false;
        IsMulticamActive = false;
        IsScenesActive = false;
        IsSettingsActive = false;
    }
}
