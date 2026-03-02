using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
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

    public MainWindowViewModel()
    {
        CurrentPage = new StudioViewModel();
    }

    [RelayCommand]
    private void ShowStudio()
    {
        CurrentPage = new StudioViewModel();
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
        CurrentPage = new SettingsViewModel();
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
