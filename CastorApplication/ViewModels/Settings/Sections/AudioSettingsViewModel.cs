using CastorApplication.Models.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CastorApplication.ViewModels.Settings.Sections;

public partial class AudioSettingsViewModel : SettingsSectionViewModel
{
    [ObservableProperty]
    private int _selectedSampleRateIndex;

    [ObservableProperty]
    private int _selectedChannelsIndex;

    [ObservableProperty]
    private int _selectedAudioBitrateIndex = 1;

    protected override void LoadCore(ApplicationSettings settings)
    {
        SelectedSampleRateIndex = settings.SelectedSampleRateIndex;
        // Migration : « 5.1 Surround » (ex-index 2) a été retiré — le moteur
        // capture en stéréo maximum.
        SelectedChannelsIndex = settings.SelectedChannelsIndex is < 0 or > 1
            ? 0
            : settings.SelectedChannelsIndex;
        SelectedAudioBitrateIndex = settings.SelectedAudioBitrateIndex;
    }

    protected override void SaveCore(ApplicationSettings settings)
    {
        settings.SelectedSampleRateIndex = SelectedSampleRateIndex;
        settings.SelectedChannelsIndex = SelectedChannelsIndex;
        settings.SelectedAudioBitrateIndex = SelectedAudioBitrateIndex;
    }
}
