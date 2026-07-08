using System.IO;
using Castor.Native;

namespace Castor.Engine.Models;

public sealed class CaptureSourceOption
{
    internal CaptureSourceInfo Info { get; }

    public string Label { get; }
    public VideoCaptureKind Type { get; }
    public string SymbolicLink { get; }
    public int Index { get; }

    /// <summary>Handle de la fenêtre capturée (sources Window uniquement) —
    /// permet à l'UI d'afficher le nom du process associé.</summary>
    public nint Hwnd { get; }

    public CaptureSourceOption(CaptureSourceInfo info)
    {
        Info = info;
        Label = info.Label;
        Hwnd = info.Hwnd;
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

    /// <summary>Handle de la fenêtre ciblée (loopback par fenêtre uniquement).</summary>
    public nint Hwnd { get; }

    public AudioSourceOption(AudioSourceInfo info)
    {
        Info = info;
        Label = info.Label;
        Hwnd = info.Hwnd;
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

/// <summary>Piste vidéo d'un fichier multimédia local.</summary>
public sealed class FileVideoSourceOption
{
    internal FileSourceInfo Info { get; }

    public string FilePath { get; }
    public string Label { get; }
    public bool Loop { get; }

    public FileVideoSourceOption(string filePath, bool loop = true)
    {
        FilePath = filePath;
        Label = Path.GetFileName(filePath);
        Loop = loop;
        Info = new FileSourceInfo { FilePath = filePath, Loop = loop };
    }
}

/// <summary>Piste audio d'un fichier multimédia local.</summary>
public sealed class FileAudioSourceOption
{
    internal FileSourceInfo Info { get; }

    public string FilePath { get; }
    public string Label { get; }
    public bool Loop { get; }

    public FileAudioSourceOption(string filePath, bool loop = true)
    {
        FilePath = filePath;
        Label = Path.GetFileName(filePath);
        Loop = loop;
        Info = new FileSourceInfo { FilePath = filePath, Loop = loop };
    }
}
