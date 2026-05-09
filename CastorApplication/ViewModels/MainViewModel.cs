using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CastorApplication.Services.Settings;

namespace CastorApplication.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private ViewModelBase? _currentPage;

    [ObservableProperty]
    private bool _isStudioActive = true;

    [ObservableProperty]
    private bool _isMulticamActive;

    [ObservableProperty]
    private bool _isScenesActive;

    [ObservableProperty]
    private bool _isSettingsActive;

    // Singleton pour préserver l'état du docking entre navigations
    private StudioViewModel? _studioPage;
    private readonly SettingsViewModel _settingsPage;

    public StudioViewModel StudioPage => _studioPage ??= new StudioViewModel();

    public MainViewModel(SettingsViewModel settingsViewModel)
    {
        _settingsPage = settingsViewModel;
        ShowStudio();
    }

    [RelayCommand]
    private void ShowStudio()
    {
        CurrentPage = StudioPage;
        ResetTabs();
        IsStudioActive = true;
    }

    [RelayCommand]
    private void ShowMulticam()
    {
        CurrentPage = new MulticamViewModel();
        ResetTabs();
        IsMulticamActive = true;
    }

    [RelayCommand]
    private void ShowScenes()
    {
        CurrentPage = new ScenesViewModel();
        ResetTabs();
        IsScenesActive = true;
    }

    [RelayCommand]
    private void ShowSettings()
    {
        CurrentPage = _settingsPage;
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
