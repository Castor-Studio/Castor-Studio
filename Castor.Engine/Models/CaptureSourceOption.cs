using Castor.Native;

namespace Castor.Engine.Models;

public sealed class CaptureSourceOption
{
    internal CaptureSourceInfo Info { get; }

    public string Label { get; }
    public VideoCaptureKind Type { get; }
    public string SymbolicLink { get; }
    public int Index { get; }

    public CaptureSourceOption(CaptureSourceInfo info)
    {
        Info = info;
        Label = info.Label;
        Type = info.Type switch
        {
            CaptureSourceType.Window => VideoCaptureKind.Window,
            CaptureSourceType.Monitor => VideoCaptureKind.Monitor,
            CaptureSourceType.Camera => VideoCaptureKind.Camera,
            CaptureSourceType.Network => VideoCaptureKind.Network,
            _ => VideoCaptureKind.Window
        };
        SymbolicLink = info.SymbolicLink;
        Index = info.Index;
    }
}

public sealed class AudioSourceOption
{
    internal AudioSourceInfo Info { get; }

    public string Label { get; }
    public AudioCaptureKind Type { get; }
    public string DeviceId { get; }
    public int Index { get; }

    public AudioSourceOption(AudioSourceInfo info)
    {
        Info = info;
        Label = info.Label;
        Type = info.Type switch
        {
            AudioSourceType.LoopbackGlobal => AudioCaptureKind.LoopbackGlobal,
            AudioSourceType.LoopbackWindow => AudioCaptureKind.LoopbackWindow,
            AudioSourceType.Microphone => AudioCaptureKind.Microphone,
            AudioSourceType.CameraMic => AudioCaptureKind.CameraMic,
            _ => AudioCaptureKind.LoopbackGlobal
        };
        DeviceId = info.DeviceId;
        Index = info.Index;
    }
}
