namespace CastorApplication.Models.Settings;

public static class VideoResolution
{
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
