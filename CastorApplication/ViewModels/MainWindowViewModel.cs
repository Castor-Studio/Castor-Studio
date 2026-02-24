using CastorApplication.ViewModels;
using CastorApplication.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private ViewModelBase? _currentPage;

    public MainWindowViewModel()
    {
        CurrentPage = new StudioViewModel();
    }

    [RelayCommand]
    private void ShowStudio() => CurrentPage = new StudioViewModel();

    [RelayCommand]
    private void ShowSettings() => CurrentPage = new SettingsViewModel();

    [RelayCommand]
    private void ShowScenes() => CurrentPage = new ScenesViewModel();

    [RelayCommand]
    private void ShowMulticam() => CurrentPage = new MulticamViewModel();
}