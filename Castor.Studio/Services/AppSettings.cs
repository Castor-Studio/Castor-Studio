namespace CastorApplication.Services;

/// <summary>
/// Settings shared between ViewModels (streaming, output).
/// </summary>
public static class AppSettings
{
    // 0 = Twitch, 1 = YouTube Live, 2 = Facebook Live, 3 = Custom
    public static int StreamPlatformIndex { get; set; } = 0;
    public static string StreamKey { get; set; } = "";
    public static string CustomRtmpUrl { get; set; } = "";
}
