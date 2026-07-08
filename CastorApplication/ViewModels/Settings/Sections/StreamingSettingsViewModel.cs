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
    private double _streamingBitrate = 4000;

    public string StreamingBitrateDisplay => $"{(int)StreamingBitrate}";

    partial void OnStreamingBitrateChanged(double value)
        => OnPropertyChanged(nameof(StreamingBitrateDisplay));

    protected override void LoadCore(ApplicationSettings settings)
    {
        // Migration : Facebook Live (ex-index 2) a été retiré du sélecteur.
        // Ancien 3 (Personnalisé) → 2 ; ancien 2 (Facebook) → 0 (Twitch).
        SelectedPlatformIndex = settings.SelectedPlatformIndex switch
        {
            3     => 2,
            2     => 0,
            var i => i,
        };
        StreamKey        = settings.StreamKey;
        RtmpUrl          = settings.RtmpUrl;
        StreamingBitrate = settings.StreamingBitrate;
    }

    protected override void SaveCore(ApplicationSettings settings)
    {
        settings.SelectedPlatformIndex = SelectedPlatformIndex;
        settings.StreamKey             = StreamKey;
        settings.RtmpUrl               = RtmpUrl;
        settings.StreamingBitrate      = StreamingBitrate;
    }
}
