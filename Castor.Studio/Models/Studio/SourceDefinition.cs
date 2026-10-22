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

    // What a hardware source captures, for its icon and label. Null for file sources and for
    // sources saved before these were recorded.
    public VideoCaptureKind? VideoCaptureKind { get; set; }
    public AudioCaptureKind? AudioCaptureKind { get; set; }
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

// A device of the local network that accepts RTMP connections. RTMP cannot list the streams
// a device serves, so only its address is known: the stream path is left to the operator.
public sealed record DiscoveredRtmpServer(string Address, string HostName = "")
{
    public string Server => $"rtmp://{Address}:{RtmpStreamUrl.DefaultPort}";
    public string Title => HostName.Length > 0 ? HostName : Address;
}
