namespace CastorApplication.Models.Studio;

public enum SourceKind
{
    Video = 0,
    Audio = 1,
    Media = 2
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
