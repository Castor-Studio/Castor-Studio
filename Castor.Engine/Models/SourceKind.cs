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

/// <summary>D'où provient une source, pour permettre de la retrouver/recréer à l'import d'un export de scènes.</summary>
public enum SourceOrigin
{
    /// <summary>Moniteur/caméra/fenêtre — retrouvé par correspondance de libellé parmi les périphériques disponibles.</summary>
    HardwareVideo,

    /// <summary>Microphone/loopback — retrouvé par correspondance de libellé.</summary>
    HardwareAudio,

    /// <summary>Flux réseau (RTMP/RTSP/HTTP) — entièrement portable via son URL.</summary>
    Network,

    /// <summary>Fichier local — entièrement portable via son chemin.</summary>
    File
}
