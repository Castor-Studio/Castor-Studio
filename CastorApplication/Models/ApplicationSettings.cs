namespace CastorApplication.Models;

public sealed class ApplicationSettings
{
    public int SelectedLanguageIndex { get; set; }
    public bool AutoStart { get; set; }
    public int SelectedThemeIndex { get; set; }

    public int SelectedBaseResolutionIndex { get; set; } = 1;
    public int SelectedOutputResolutionIndex { get; set; }
    public int SelectedFpsIndex { get; set; }
    public double VideoBitrate { get; set; } = 6000;

    public int SelectedSampleRateIndex { get; set; }
    public int SelectedChannelsIndex { get; set; }
    public int SelectedAudioBitrateIndex { get; set; } = 1;

    public int SelectedPlatformIndex { get; set; }
    public string StreamKey { get; set; } = "";
    public string RtmpUrl { get; set; } = "";
    public int SelectedServerIndex { get; set; }

    public bool IsTwitchConnected { get; set; }
    public bool IsYoutubeConnected { get; set; }
    public bool IsFacebookConnected { get; set; }

    public int SelectedOutputFormatIndex { get; set; }
    public string OutputPath { get; set; } = "";
}
