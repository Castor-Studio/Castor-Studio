using CastorApplication.Models.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CastorApplication.ViewModels.Settings.Sections;

public partial class StreamingSettingsViewModel : SettingsSectionViewModel
{
    [ObservableProperty]
    private int _selectedPlatformIndex;

    [ObservableProperty]
    private string _streamKey = "";

    [ObservableProperty]
    private string _rtmpUrl = "";

    [ObservableProperty]
    private int _selectedServerIndex;

    protected override void LoadCore(ApplicationSettings settings)
    {
        SelectedPlatformIndex = settings.SelectedPlatformIndex;
        StreamKey = settings.StreamKey;
        RtmpUrl = settings.RtmpUrl;
        SelectedServerIndex = settings.SelectedServerIndex;
    }

    protected override void SaveCore(ApplicationSettings settings)
    {
        settings.SelectedPlatformIndex = SelectedPlatformIndex;
        settings.StreamKey = StreamKey;
        settings.RtmpUrl = RtmpUrl;
        settings.SelectedServerIndex = SelectedServerIndex;
    }
}
