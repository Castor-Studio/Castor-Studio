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

    // Given to every platform at each live start. Twitch has no description of a live: the
    // description and the visibility only go to the YouTube broadcast.
    public string LiveTitle { get; set; } = DefaultLiveTitle;
    public string LiveDescription { get; set; } = "";
    public YouTubePrivacy YouTubePrivacy { get; set; } = YouTubePrivacy.Public;

    public const string DefaultLiveTitle = "Live Castor Studio";

    // The strictest platform sets the limits: YouTube (Twitch takes titles up to 140).
    public const int LiveTitleMaxLength = 100;
    public const int LiveDescriptionMaxLength = 5000;

    // A blank title, a longer one, or one with '<' or '>' would make YouTube refuse the broadcast.
    public static string NormalizeLiveTitle(string? title)
    {
        var cleaned = WithoutAngleBrackets(title).Trim();
        return cleaned.Length == 0
            ? DefaultLiveTitle
            : cleaned[..Math.Min(cleaned.Length, LiveTitleMaxLength)];
    }

    // Same YouTube rules as the title, but a description may stay empty.
    public static string NormalizeLiveDescription(string? description)
    {
        var cleaned = WithoutAngleBrackets(description).Trim();
        return cleaned[..Math.Min(cleaned.Length, LiveDescriptionMaxLength)];
    }

    private static string WithoutAngleBrackets(string? text) =>
        (text ?? "").Replace("<", "").Replace(">", "");

    // Chosen in the "Scène active" bar; applies to live and recording.
    public SceneTransitionKind SceneTransitionKind { get; set; } = SceneTransition.Default.Kind;
    public int SceneTransitionDurationMs { get; set; } = SceneTransition.DefaultDurationMs;

    public SceneTransition ToSceneTransition() =>
        SceneTransition.From(SceneTransitionKind, SceneTransitionDurationMs);

    // Read by the AI on each start: a change applies to the next one.
    public AiDevice AiDevice { get; set; } = AiDevice.Auto;

    public int SelectedOutputFormatIndex { get; set; }
    public string OutputPath { get; set; } = DefaultOutputPath;

    public static string DefaultOutputPath =>
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
}
