namespace CastorApplication.Models.Settings;

public sealed class ApplicationSettings
{
    public int SelectedLanguageIndex { get; set; }
    public bool AutoStart { get; set; }
    public int SelectedThemeIndex { get; set; }

    public int SelectedBaseResolutionIndex { get; set; } = 1;
    public int SelectedOutputResolutionIndex { get; set; }
    public int SelectedFpsIndex { get; set; }
    public double VideoBitrate { get; set; } = 6000;
    public double StreamingBitrate { get; set; } = 4000;

    public int RecordingQualityIndex { get; set; } = 1; // 0=haute 1=bonne 2=basse

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

    /* ── Lecture (preview player) ── */
    /// <summary>Volume du player de prévisualisation, de 0 à 100.</summary>
    public double PlayerVolume { get; set; } = 80;

    /// <summary>Coupe automatiquement le son du player quand un enregistrement ou un live démarre.</summary>
    public bool MutePlayersOnRecord { get; set; } = true;
}
