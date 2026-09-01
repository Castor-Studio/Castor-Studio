using Avalonia.Media;
using Avalonia.Threading;
using Castor.Engine.Services;
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
    private readonly IStudioController _studioController;

    // ── Badge d'état global (navbar) ─────────────────────────────────────────

    public string GlobalStatusText => _studioController.IsStreaming ? "EN DIRECT"
                                    : _studioController.IsRecording ? "REC"
                                    : "OFFLINE";

    public IBrush GlobalStatusBrush => _studioController.IsStreaming || _studioController.IsRecording
        ? SolidColorBrush.Parse("#f87171")
        : SolidColorBrush.Parse("#3c3c4e");

    private void NotifyGlobalStatusChanged()
    {
        // StreamingStarted peut être levé depuis un thread de fond (StartStream
        // tourne dans un Task.Run) : on repasse sur le thread UI.
        Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(GlobalStatusText));
            OnPropertyChanged(nameof(GlobalStatusBrush));
        });
    }

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
        SettingsViewModel settingsViewModel,
        IStudioController studioController)
    {
        _studioViewModel = studioViewModel;
        _multicamViewModel = multicamViewModel;
        _scenesViewModel = scenesViewModel;
        _settingsViewModel = settingsViewModel;
        _studioController = studioController;

        _studioController.RecordingStarted += NotifyGlobalStatusChanged;
        _studioController.RecordingStopped += NotifyGlobalStatusChanged;
        _studioController.StreamingStarted += NotifyGlobalStatusChanged;
        _studioController.StreamingStopped += NotifyGlobalStatusChanged;

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
