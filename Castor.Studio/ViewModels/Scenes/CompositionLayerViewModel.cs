using CastorApplication.Models.Studio;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CastorApplication.ViewModels.Scenes;

/// <summary>
/// Une source telle que le canvas de composition la dessine. Le rectangle vient entièrement
/// du moteur : rien n'est ajusté ici, l'affichage se contente de le poser sur le canvas.
/// </summary>
public partial class CompositionLayerViewModel : ViewModelBase
{
    public Guid SourceId { get; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _color = "#5b8def";
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private double _width;
    [ObservableProperty] private double _height;
    [ObservableProperty] private string _geometry = "";

    internal CompositionLayerViewModel(SourceTransform transform, string name, string color)
    {
        SourceId = transform.SourceId;
        Apply(transform, name, color);
    }

    /// <summary>
    /// Recale ce calque sur une nouvelle lecture du moteur. Les calques sont mis à jour
    /// plutôt que recréés : le canvas se rafraîchit en continu, et refaire les visuels à
    /// chaque lecture les ferait clignoter.
    /// </summary>
    internal void Apply(SourceTransform transform, string name, string color)
    {
        Name = name;
        Color = color;
        X = transform.X;
        Y = transform.Y;
        Width = transform.Width;
        Height = transform.Height;
        Geometry = Describe(transform);
    }

    private static string Describe(SourceTransform transform)
    {
        var size = $"{Math.Round(transform.Width)} × {Math.Round(transform.Height)}";
        return transform.Crop == SourceCrop.None ? size : $"{size} · rognée";
    }
}
