using CommunityToolkit.Mvvm.ComponentModel;

namespace Castor.Engine.Models;

public partial class SourceItem : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private SourceKind _kind = SourceKind.Video;

    [ObservableProperty]
    private string _color = "#5b8def";

    [ObservableProperty]
    private bool _isActive = true;

    public string Type => Kind == SourceKind.Video ? "Vidéo" : "Audio";

    internal IntPtr NativePtr { get; set; } = IntPtr.Zero;

    internal object? NativeDescriptor { get; set; }

    public SourceItem()
    {
    }

    public SourceItem(string name, SourceKind kind, string color)
    {
        Name = name;
        Kind = kind;
        Color = color;
    }

    partial void OnKindChanged(SourceKind value) => OnPropertyChanged(nameof(Type));
}
