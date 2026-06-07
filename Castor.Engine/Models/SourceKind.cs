namespace Castor.Engine.Models;

public enum SourceKind
{
    Video,
    Audio
}

public enum VideoCaptureKind
{
    Window,
    Monitor,
    Camera,
    Network
}

public enum AudioCaptureKind
{
    LoopbackGlobal,
    LoopbackWindow,
    Microphone,
    CameraMic
}

public enum StreamingPlatform
{
    Custom,
    Twitch,
    YouTube
}
