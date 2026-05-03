using Castor.Native;

namespace CastorApplication.Models;

/// <summary>Wrapper de CaptureSourceInfo pour le binding Avalonia (propriétés vs champs).</summary>
public class CaptureSourceOption
{
    public string Label { get; }
    public CaptureSourceType Type { get; }
    public CaptureSourceInfo Info { get; }

    public CaptureSourceOption(CaptureSourceInfo info)
    {
        Label = info.Label ?? "";
        Type  = info.Type;
        Info  = info;
    }
}

/// <summary>Wrapper de AudioSourceInfo pour le binding Avalonia.</summary>
public class AudioSourceOption
{
    public string Label { get; }
    public AudioSourceType Type { get; }
    public AudioSourceInfo Info { get; }

    public AudioSourceOption(AudioSourceInfo info)
    {
        Label = info.Label ?? "";
        Type  = info.Type;
        Info  = info;
    }
}
