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
    public string Type => Kind switch
    {
        SourceKind.Video => "Vidéo",
        SourceKind.Audio => "Audio",
        SourceKind.Media => "Média",
        _ => "Source"
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
        OriginPath = OriginPath
    };

    internal void RefreshLoopState() => OnPropertyChanged(nameof(Loop));

    partial void OnKindChanged(SourceKind value) => OnPropertyChanged(nameof(Type));
}
