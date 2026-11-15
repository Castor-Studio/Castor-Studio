namespace CastorApplication.Models.Settings;

public readonly record struct VideoCanvasResolution(int Width, int Height)
{
    public bool IsValid => Width > 0 && Height > 0;
}

public sealed record BaseResolutionOption(
    string Label,
    VideoCanvasResolution Resolution,
    int? LegacyIndex);
