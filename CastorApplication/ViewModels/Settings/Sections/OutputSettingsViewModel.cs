using CastorApplication.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CastorApplication.ViewModels.Settings.Sections;

public partial class OutputSettingsViewModel : SettingsSectionViewModel
{
    [ObservableProperty]
    private int _selectedOutputFormatIndex;

    [ObservableProperty]
    private string _outputPath = "";

    protected override void LoadCore(ApplicationSettings settings)
    {
        SelectedOutputFormatIndex = settings.SelectedOutputFormatIndex;
        OutputPath = settings.OutputPath;
    }

    protected override void SaveCore(ApplicationSettings settings)
    {
        settings.SelectedOutputFormatIndex = SelectedOutputFormatIndex;
        settings.OutputPath = OutputPath;
    }
}
