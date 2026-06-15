using CastorApplication.Models.Settings;
using CastorApplication.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CastorApplication.ViewModels.Settings.Sections;

public partial class GeneralSettingsViewModel : SettingsSectionViewModel
{
    private readonly IThemeService _themeService;

    public GeneralSettingsViewModel(IThemeService themeService)
    {
        _themeService = themeService;
    }

    [ObservableProperty]
    private int _selectedLanguageIndex;

    [ObservableProperty]
    private bool _autoStart;

    [ObservableProperty]
    private int _selectedThemeIndex;

    partial void OnSelectedThemeIndexChanged(int value)
    {
        _themeService.ApplyTheme(value);
    }

    protected override void LoadCore(ApplicationSettings settings)
    {
        SelectedLanguageIndex = settings.SelectedLanguageIndex;
        AutoStart = settings.AutoStart;

        SelectedThemeIndex = settings.SelectedThemeIndex is < 0 or > 1
            ? _themeService.IsLightTheme ? 1 : 0
            : settings.SelectedThemeIndex;
    }

    protected override void SaveCore(ApplicationSettings settings)
    {
        settings.SelectedLanguageIndex = SelectedLanguageIndex;
        settings.AutoStart = AutoStart;
        settings.SelectedThemeIndex = SelectedThemeIndex;
    }
}
