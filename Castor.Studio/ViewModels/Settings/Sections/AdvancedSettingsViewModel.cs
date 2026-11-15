using CastorApplication.Models.Settings;
using CastorApplication.Services.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.ViewModels.Settings.Sections;

public partial class AdvancedSettingsViewModel : SettingsSectionViewModel
{
    // Logs and crash reports: what a tester attaches to a bug report.
    public string LogsDirectory => AppLog.Directory;

    [RelayCommand]
    private void OpenLogsFolder() => AppInfo.ShowInExplorer(LogsDirectory);

    // Index in the combo box: 0 = Auto, 1 = CPU, 2 = GPU, the order of AiDevice. Not applied
    // here: the AI reads the saved value on its next start.
    [ObservableProperty]
    private int _selectedAiDeviceIndex;

    protected override void LoadCore(ApplicationSettings settings)
    {
        SelectedAiDeviceIndex = Enum.IsDefined(settings.AiDevice) ? (int)settings.AiDevice : (int)AiDevice.Auto;
    }

    protected override void SaveCore(ApplicationSettings settings)
    {
        settings.AiDevice = Enum.IsDefined((AiDevice)SelectedAiDeviceIndex)
            ? (AiDevice)SelectedAiDeviceIndex
            : AiDevice.Auto;
    }
}
