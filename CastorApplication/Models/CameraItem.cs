using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LibVLCSharp.Shared;

namespace CastorApplication.Models;

public partial class CameraItem : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _label = "";

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isLive;

    /// <summary>
    /// Source de la caméra. Peut être un chemin de fichier local (mode démo IA podcast)
    /// ou une URL RTMP/RTSP. Null quand la caméra est un placeholder (pas encore lié).
    /// </summary>
    [ObservableProperty]
    private string? _sourceUri;

    /// <summary>VLC player that drives the per-card video preview. Muted by default.</summary>
    [ObservableProperty]
    private MediaPlayer? _mediaPlayer;

    /// <summary>Currently-displayed thumbnail frame (updates via timer for animation).</summary>
    [ObservableProperty]
    private Bitmap? _thumbnail;

    /// <summary>Sequence of pre-extracted frames; cycled to fake video playback.</summary>
    public System.Collections.Generic.List<Bitmap>? FrameSequence { get; set; }

    public CameraItem() { }

    public CameraItem(string label, string name, bool isActive = false, bool isLive = false,
                      string? sourceUri = null, MediaPlayer? mediaPlayer = null)
    {
        Label        = label;
        Name         = name;
        IsActive     = isActive;
        IsLive       = isLive;
        SourceUri    = sourceUri;
        MediaPlayer  = mediaPlayer;
    }

    /// <summary>Stop and dispose the player (call before clearing the camera list).</summary>
    public void DisposePlayer()
    {
        var p = MediaPlayer;
        MediaPlayer = null;
        if (p == null) return;
        try { if (p.IsPlaying) p.Stop(); } catch { /* ignore */ }
        try { p.Dispose(); } catch { /* ignore */ }
    }
}
