namespace CastorApplication.Models.Settings;

public static class VideoResolution
{
    private static readonly BaseResolutionOption[] BasePresets =
    [
        new("3840x2160 (4K)", new(3840, 2160), 0),
        new("1920x1080 (Full HD)", new(1920, 1080), 1),
        new("1280x720 (HD)", new(1280, 720), 2),
        new("2560x1440 (2K)", new(2560, 1440), 3)
    ];

    public static IReadOnlyList<BaseResolutionOption> BaseOptions(VideoCanvasResolution monitorResolution)
    {
        var monitorIndex = Array.FindIndex(
            BasePresets,
            option => option.Resolution == monitorResolution);
        var options = new List<BaseResolutionOption>
        {
            new(
                $"{monitorResolution.Width}x{monitorResolution.Height} (Moniteur principal)",
                monitorResolution,
                monitorIndex >= 0 ? monitorIndex : null)
        };

        foreach (var preset in BasePresets)
        {
            if (preset.Resolution != monitorResolution)
                options.Add(preset);
        }

        return options;
    }

    public static (int Width, int Height) BaseFromIndex(int index) => index switch
    {
        0 => (3840, 2160),
        2 => (1280, 720),
        3 => (2560, 1440),
        _ => (1920, 1080)
    };

    public static (int Width, int Height) OutputFromIndex(int index) => index switch
    {
        1 => (1280, 720),
        2 => (854, 480),
        3 => (2560, 1440),
        _ => (1920, 1080)
    };
}
