namespace CastorApplication.Models.Studio;

public sealed class SourceDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public SourceKind Kind { get; set; }
    public string Color { get; set; } = "#5b8def";
    public bool Loop { get; set; } = true;
    public SourceOrigin Origin { get; set; }
    public string OriginLabel { get; set; } = "";
    public string OriginPath { get; set; } = "";
}

public sealed record CaptureSourceOption(
    string Id,
    string Label,
    VideoCaptureKind Type,
    string DevicePath = "",
    int Index = -1);

public sealed record AudioSourceOption(
    string Id,
    string Label,
    AudioCaptureKind Type,
    string DeviceId = "",
    int Index = -1);

public sealed record DiscoveredCamera(
    string Label,
    string Ip,
    string SuggestedUrl,
    string Method);
