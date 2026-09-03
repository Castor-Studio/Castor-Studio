using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
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
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;

    // The menu bar's "Panneaux" entries. Choosing one goes to the Studio page first, since that
    // is where the panel it brings back lives.
    public IReadOnlyList<StudioPanelEntry> PanelMenu { get; }

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
        StudioWorkspaceViewModel workspace,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        _studioViewModel = studioViewModel;
        _studioDockViewModel = studioDockViewModel;
        _multicamViewModel = multicamViewModel;
        _scenesViewModel = scenesViewModel;
        _settingsViewModel = settingsViewModel;
        _workspace = workspace;
        _desktop = desktop;
        _workspace.PropertyChanged += OnWorkspacePropertyChanged;

        PanelMenu =
        [
            .. _studioDockViewModel.Panels.Select(panel =>
                new StudioPanelEntry(panel.Title, new RelayCommand(() => ShowPanel(panel.Id)))),
            new StudioPanelEntry("Réinitialiser la disposition", new RelayCommand(ResetStudioLayout)),
        ];

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

    [RelayCommand]
    private void Quit() => _desktop.Shutdown();

    public void ApplyScreenSize(Size screenSize) => _studioDockViewModel.ApplyScreenSize(screenSize);

    public void PresentFloatingPanels() => _studioDockViewModel.PresentFloatingPanels();

    private void ShowPanel(string id)
    {
        ShowStudio();
        _studioDockViewModel.ShowPanel(id);
    }

    private void ResetStudioLayout()
    {
        ShowStudio();
        _studioDockViewModel.ResetLayout();
    }

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
