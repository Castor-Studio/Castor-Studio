using CastorApplication.Models;
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
        SelectedChannelsIndex = settings.SelectedChannelsIndex;
        SelectedAudioBitrateIndex = settings.SelectedAudioBitrateIndex;
    }

    protected override void SaveCore(ApplicationSettings settings)
    {
        settings.SelectedSampleRateIndex = SelectedSampleRateIndex;
        settings.SelectedChannelsIndex = SelectedChannelsIndex;
        settings.SelectedAudioBitrateIndex = SelectedAudioBitrateIndex;
    }
}
