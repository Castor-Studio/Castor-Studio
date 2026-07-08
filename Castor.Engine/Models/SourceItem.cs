using Castor.Native;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Castor.Engine.Models;

public partial class SourceItem : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private SourceKind _kind = SourceKind.Video;

    [ObservableProperty]
    private string _color = "#5b8def";

    [ObservableProperty]
    private bool _isActive = true;

    /// <summary>Boucle automatique — pertinent uniquement pour les sources fichier.</summary>
    [ObservableProperty]
    private bool _loop = true;

    public string Type => Kind == SourceKind.Video ? "Vidéo" : "Audio";

    /// <summary>Vrai si la source provient d'un fichier local.</summary>
    public bool IsFileSource => NativeDescriptor is FileSourceInfo;

    /// <summary>D'où provient cette source — permet de la retrouver/recréer lors d'un import.</summary>
    public SourceOrigin Origin { get; internal set; }

    /// <summary>Libellé du périphérique (hardware/réseau) utilisé pour la retrouver à l'import.</summary>
    public string OriginLabel { get; internal set; } = "";

    /// <summary>URL réseau ou chemin de fichier local, selon <see cref="Origin"/>. Vide pour une source matérielle.</summary>
    public string OriginPath { get; internal set; } = "";

    internal IntPtr NativePtr { get; set; } = IntPtr.Zero;

    public object? NativeDescriptor { get; internal set; }

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
