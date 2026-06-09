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
    Network,
    File,
}

public enum AudioCaptureKind
{
    LoopbackGlobal,
    LoopbackWindow,
    Microphone,
    CameraMic,
    File,
}

public enum StreamingPlatform
{
    Custom,
    Twitch,
    YouTube
}
