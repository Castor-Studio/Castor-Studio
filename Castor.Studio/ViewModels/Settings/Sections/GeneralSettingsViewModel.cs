using CastorApplication.Models.Settings;
using CastorApplication.Services;
using CastorApplication.Services.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.ViewModels.Settings.Sections;

public partial class GeneralSettingsViewModel : SettingsSectionViewModel
{
    private readonly IThemeService _themeService;
    private readonly IDockChromeService _dockChromeService;

    public GeneralSettingsViewModel(IThemeService themeService, IDockChromeService dockChromeService)
    {
        _themeService = themeService;
        _dockChromeService = dockChromeService;
    }

    [ObservableProperty]
    private int _selectedThemeIndex;

    [ObservableProperty]
    private bool _showDockTitles = true;

    // What a tester quotes in a bug report, and where the files to attach are.
    public string VersionText => $"Version {AppInfo.Version}";
    public string LogsDirectory => AppLog.Directory;

    [RelayCommand]
    private void OpenLogsFolder() => AppInfo.ShowInExplorer(LogsDirectory);

    partial void OnSelectedThemeIndexChanged(int value)
    {
        _themeService.ApplyTheme(value);
    }

    partial void OnShowDockTitlesChanged(bool value)
    {
        _dockChromeService.ApplyShowTitles(value);
    }

    protected override void LoadCore(ApplicationSettings settings)
    {
        SelectedThemeIndex = settings.SelectedThemeIndex is < 0 or > 1
            ? _themeService.IsLightTheme ? 1 : 0
            : settings.SelectedThemeIndex;
        ShowDockTitles = settings.ShowDockTitles;
        // The setter above only fires the change hook when the value differs from the
        // default, so push the loaded value explicitly to cover startup.
        _dockChromeService.ApplyShowTitles(ShowDockTitles);
    }

    protected override void SaveCore(ApplicationSettings settings)
    {
        settings.SelectedThemeIndex = SelectedThemeIndex;
        settings.ShowDockTitles = ShowDockTitles;
    }
}
