using Avalonia;
using Avalonia.Styling;
using CastorApplication.Models.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CastorApplication.ViewModels.Settings.Sections;

public partial class GeneralSettingsViewModel : SettingsSectionViewModel
{
    [ObservableProperty]
    private int _selectedLanguageIndex;

    [ObservableProperty]
    private bool _autoStart;

    [ObservableProperty]
    private int _selectedThemeIndex;

    partial void OnSelectedThemeIndexChanged(int value)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant =
            value == 1 ? ThemeVariant.Light : ThemeVariant.Dark;
    }

    protected override void LoadCore(ApplicationSettings settings)
    {
        SelectedLanguageIndex = settings.SelectedLanguageIndex;
        AutoStart = settings.AutoStart;

        SelectedThemeIndex = settings.SelectedThemeIndex is < 0 or > 1
            ? Application.Current?.RequestedThemeVariant == ThemeVariant.Light ? 1 : 0
            : settings.SelectedThemeIndex;
    }

    protected override void SaveCore(ApplicationSettings settings)
    {
        settings.SelectedLanguageIndex = SelectedLanguageIndex;
        settings.AutoStart = AutoStart;
        settings.SelectedThemeIndex = SelectedThemeIndex;
    }
}
