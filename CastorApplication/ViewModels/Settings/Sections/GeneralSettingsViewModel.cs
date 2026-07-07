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
    private int _selectedThemeIndex;

    partial void OnSelectedThemeIndexChanged(int value)
    {
        _themeService.ApplyTheme(value);
    }

    protected override void LoadCore(ApplicationSettings settings)
    {
        SelectedThemeIndex = settings.SelectedThemeIndex is < 0 or > 1
            ? _themeService.IsLightTheme ? 1 : 0
            : settings.SelectedThemeIndex;
    }

    protected override void SaveCore(ApplicationSettings settings)
    {
        settings.SelectedThemeIndex = SelectedThemeIndex;
    }
}
