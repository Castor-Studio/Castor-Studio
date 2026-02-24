using CastorApplication.ViewModels;
using CastorApplication.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private ViewModelBase? _currentPage;

    private StudioViewModel? _studioPage;

    public MainWindowViewModel()
    {
        // Au démarrage, on appelle la méthode qui gère l'instance unique
        ShowStudio();
    }

    [RelayCommand]
    private void ShowStudio()
    {
        // Si c'est la première fois qu'on clique, on crée l'objet
        // Les fois suivantes, on réutilise celui qui est stocké dans _studioPage
        if (_studioPage == null)
        {
            _studioPage = new StudioViewModel();
        }

        CurrentPage = _studioPage;
    }

    [RelayCommand]
    private void ShowSettings() => CurrentPage = new SettingsViewModel();

    [RelayCommand]
    private void ShowScenes() => CurrentPage = new ScenesViewModel();

    [RelayCommand]
    private void ShowMulticam() => CurrentPage = new MulticamViewModel();
}