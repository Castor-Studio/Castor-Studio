namespace CastorApplication.Models.Studio;

public enum SourceKind
{
    Video,
    Audio
}

public enum SourceOrigin
{
    HardwareVideo,
    HardwareAudio,
    Network,
    File
}

public enum VideoCaptureKind
{
    Window,
    Monitor,
    Camera,
    Network,
    File
}

public enum AudioCaptureKind
{
    LoopbackGlobal,
    LoopbackWindow,
    Microphone,
    CameraMic,
    File
}

public enum StreamingPlatform
{
    Custom,
    Twitch,
    YouTube
}
