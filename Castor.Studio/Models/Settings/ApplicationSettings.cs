using CastorApplication.Models.Studio;

namespace CastorApplication.Models.Settings;

public sealed class ApplicationSettings
{
    public int SelectedThemeIndex { get; set; }
    public bool ShowDockTitles { get; set; } = true;

    public int SelectedBaseResolutionIndex { get; set; } = 1;
    public int? BaseCanvasWidth { get; set; }
    public int? BaseCanvasHeight { get; set; }
    public int SelectedOutputResolutionIndex { get; set; }
    public int SelectedFpsIndex { get; set; }
    public double VideoBitrate { get; set; } = 6000;
    public double StreamingBitrate { get; set; } = 4000;

    public int SelectedSampleRateIndex { get; set; }
    public int SelectedChannelsIndex { get; set; }
    public int SelectedAudioBitrateIndex { get; set; } = 1;

    // Platforms the live goes to, all at once (StreamingProviders ids). Those not ready to stream
    // are skipped; none left: the first connected account in StreamingProviders.All order.
    public List<string> StreamingProviderIds { get; set; } = [];

    // YouTube broadcast created at each live start.
    public string YouTubeBroadcastTitle { get; set; } = DefaultYouTubeBroadcastTitle;
    public YouTubePrivacy YouTubePrivacy { get; set; } = YouTubePrivacy.Public;

    public const string DefaultYouTubeBroadcastTitle = "Live Castor Studio";

    // YouTube refuses longer titles.
    public const int YouTubeBroadcastTitleMaxLength = 100;

    // Chosen in the "Scène active" bar; applies to live and recording.
    public SceneTransitionKind SceneTransitionKind { get; set; } = SceneTransition.Default.Kind;
    public int SceneTransitionDurationMs { get; set; } = SceneTransition.DefaultDurationMs;

    public SceneTransition ToSceneTransition() =>
        SceneTransition.From(SceneTransitionKind, SceneTransitionDurationMs);

    public int SelectedOutputFormatIndex { get; set; }
    public string OutputPath { get; set; } = DefaultOutputPath;

    public static string DefaultOutputPath =>
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
}
