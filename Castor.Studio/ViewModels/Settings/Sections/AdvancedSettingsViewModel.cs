using CastorApplication.Models.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CastorApplication.ViewModels.Settings.Sections;

public partial class AdvancedSettingsViewModel : SettingsSectionViewModel
{
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
