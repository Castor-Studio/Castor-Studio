using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CastorApplication.Models;

public partial class SourceItem : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _type = "Vidéo";

    [ObservableProperty]
    private string _color = "#5b8def";

    [ObservableProperty]
    private bool _isActive = true;

    public IBrush ColorBrush => SolidColorBrush.Parse(Color);

    partial void OnColorChanged(string value) => OnPropertyChanged(nameof(ColorBrush));

    public SourceItem() { }

    /// <summary>Pointeur vers le source_t* natif. IntPtr.Zero si source purement UI.</summary>
    public IntPtr NativePtr { get; set; } = IntPtr.Zero;

    /// <summary>
    /// Infos natives brutes (CaptureSourceInfo ou AudioSourceInfo).
    /// Utilisé pour initialiser la capture au démarrage de l'enregistrement.
    /// </summary>
    public object? Tag { get; set; }

    public SourceItem(string name, string type, string color)
    {
        Name = name;
        Type = type;
        Color = color;
    }
}
