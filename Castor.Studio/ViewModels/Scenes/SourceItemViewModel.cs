using CastorApplication.Models.Studio;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CastorApplication.ViewModels.Scenes;

public partial class SourceItemViewModel : ViewModelBase
{
    public Guid Id { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private SourceKind _kind;

    [ObservableProperty]
    private string _color;

    [ObservableProperty]
    private bool _isActive = true;

    [ObservableProperty]
    private bool _loop;

    public SourceOrigin Origin { get; }
    public string OriginLabel { get; }
    public string OriginPath { get; }
    public VideoCaptureKind? VideoCaptureKind { get; }
    public AudioCaptureKind? AudioCaptureKind { get; }

    public string Type => Kind switch
    {
        SourceKind.Video => VideoCaptureKind switch
        {
            Models.Studio.VideoCaptureKind.Camera => "Caméra",
            Models.Studio.VideoCaptureKind.Monitor => "Écran",
            Models.Studio.VideoCaptureKind.Window => "Fenêtre",
            _ => "Vidéo"
        },
        SourceKind.Audio => AudioCaptureKind switch
        {
            Models.Studio.AudioCaptureKind.Microphone or Models.Studio.AudioCaptureKind.CameraMic => "Micro",
            Models.Studio.AudioCaptureKind.LoopbackGlobal or Models.Studio.AudioCaptureKind.LoopbackWindow => "Audio système",
            _ => "Audio"
        },
        SourceKind.Media => "Média",
        _ => "Source"
    };

    // Sources saved before the capture kind was recorded fall back to the generic icon of
    // their kind: a camera for video, a speaker for audio.
    public string IconPath => Kind switch
    {
        SourceKind.Video => VideoCaptureKind switch
        {
            Models.Studio.VideoCaptureKind.Monitor => SourceIcons.Monitor,
            Models.Studio.VideoCaptureKind.Window => SourceIcons.Window,
            _ => SourceIcons.Camera
        },
        SourceKind.Audio => AudioCaptureKind is Models.Studio.AudioCaptureKind.Microphone or Models.Studio.AudioCaptureKind.CameraMic
            ? SourceIcons.Mic
            : SourceIcons.Volume,
        _ => SourceIcons.Movie
    };

    public bool IsFileSource => Origin == SourceOrigin.File;

    public SourceItemViewModel(SourceDefinition source)
    {
        Id = source.Id;
        _name = source.Name;
        _kind = source.Kind;
        _color = source.Color;
        _loop = source.Loop;
        Origin = source.Origin;
        OriginLabel = source.OriginLabel;
        OriginPath = source.OriginPath;
        VideoCaptureKind = source.VideoCaptureKind;
        AudioCaptureKind = source.AudioCaptureKind;
    }

    public SourceDefinition ToDefinition() => new()
    {
        Id = Id,
        Name = Name,
        Kind = Kind,
        Color = Color,
        Loop = Loop,
        Origin = Origin,
        OriginLabel = OriginLabel,
        OriginPath = OriginPath,
        VideoCaptureKind = VideoCaptureKind,
        AudioCaptureKind = AudioCaptureKind
    };

    internal void RefreshLoopState() => OnPropertyChanged(nameof(Loop));

    partial void OnKindChanged(SourceKind value)
    {
        OnPropertyChanged(nameof(Type));
        OnPropertyChanged(nameof(IconPath));
    }
}
