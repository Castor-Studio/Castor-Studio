using CastorApplication.Models.Settings;
using CastorApplication.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.ViewModels.Settings.Sections;

public partial class OutputSettingsViewModel : SettingsSectionViewModel
{
    private readonly IFilePickerService _filePickerService;

    [ObservableProperty]
    private int _selectedOutputFormatIndex;

    [ObservableProperty]
    private string _outputPath = "";

    public OutputSettingsViewModel(IFilePickerService filePickerService)
    {
        _filePickerService = filePickerService;
    }

    [RelayCommand]
    private async Task BrowseOutputPath()
    {
        var path = await _filePickerService.PickRecordingOutputFolderAsync(OutputPath);
        if (path != null) OutputPath = path;
    }

    protected override void LoadCore(ApplicationSettings settings)
    {
        SelectedOutputFormatIndex = settings.SelectedOutputFormatIndex;
        OutputPath = string.IsNullOrWhiteSpace(settings.OutputPath)
            ? ApplicationSettings.DefaultOutputPath
            : settings.OutputPath;
    }

    protected override void SaveCore(ApplicationSettings settings)
    {
        settings.SelectedOutputFormatIndex = SelectedOutputFormatIndex;
        settings.OutputPath = OutputPath;
    }
}
