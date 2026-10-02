using System.Globalization;

namespace CastorApplication.Models.Studio;

/// <summary>
/// Rognage que le moteur applique à une source, en pixels de la source et avant mise à
/// l'échelle.
/// </summary>
public readonly record struct SourceCrop(int Left, int Top, int Right, int Bottom)
{
    public static SourceCrop None { get; } = new(0, 0, 0, 0);
}

/// <summary>
/// Transformation qu'une source subit dans le canvas de sa scène, telle que le moteur la
/// détient. <see cref="Width"/> et <see cref="Height"/> sont le rectangle que le moteur
/// compose à partir du rognage et de l'échelle, <em>avant</em> rotation : la source tourne
/// ensuite de <see cref="Rotation"/> degrés, dans le sens des aiguilles d'une montre, autour
/// de son point (<see cref="X"/>, <see cref="Y"/>) — le coin haut-gauche, alignement par
/// défaut de libobs.
/// </summary>
/// <remarks>
/// libobs connaît aussi des « bounds » (un cadre qui contraint la taille rendue). L'interface
/// ne les pose pas ; s'il fallait les lire, ils changeraient le rectangle rendu ici même, et
/// rien dans l'interface.
/// </remarks>
public sealed record SourceTransform(
    Guid SourceId,
    double X,
    double Y,
    double Width,
    double Height,
    double ScaleX,
    double ScaleY,
    SourceCrop Crop,
    int SourceWidth,
    int SourceHeight,
    bool IsVisible,
    double Rotation = 0)
{
    /// <summary>Ce que le moteur détient de cette transformation, sans ce qu'il en déduit.</summary>
    public SourcePlacement Placement => new(X, Y, ScaleX, ScaleY, Crop, Rotation);
}

/// <summary>
/// Placement qu'on demande au moteur pour une source : position dans le canvas, échelle,
/// rognage et rotation, en degrés. Ce sont les seules grandeurs qu'il accepte ; le rectangle
/// composé, lui, reste sa déduction et se relit dans le <see cref="SourceTransform"/> qu'il
/// confirme.
/// </summary>
public readonly record struct SourcePlacement(
    double X,
    double Y,
    double ScaleX,
    double ScaleY,
    SourceCrop Crop,
    double Rotation = 0);

/// <summary>
/// La couleur d'une source, celle de sa pastille dans la liste, prête pour le moteur.
/// </summary>
public readonly record struct OverlayTint(float Red, float Green, float Blue)
{
    public static OverlayTint Default { get; } = new(0.357f, 0.553f, 0.937f);

    /// <summary>
    /// Lit une couleur « #rrggbb ». Une couleur illisible rend la couleur par défaut : un
    /// cadre sans couleur exacte reste plus utile qu'une source sans cadre.
    /// </summary>
    public static OverlayTint Parse(string? hex)
    {
        if (hex == null) return Default;

        var value = hex.AsSpan().TrimStart('#');
        if (value.Length != 6) return Default;

        return byte.TryParse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red) &&
               byte.TryParse(value[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) &&
               byte.TryParse(value[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue)
            ? new OverlayTint(red / 255f, green / 255f, blue / 255f)
            : Default;
    }
}

/// <summary>
/// Une source à tracer : le rectangle que le moteur lui donne, et la couleur sous laquelle
/// l'interface la nomme ailleurs.
/// </summary>
public sealed record OverlaySource(SourceTransform Transform, OverlayTint Tint);

/// <summary>
/// Une ligne d'alignement sur laquelle une source déplacée vient de s'accrocher, tracée
/// d'un bord à l'autre du canvas : verticale à l'abscisse <see cref="Position"/>, ou
/// horizontale à cette ordonnée.
/// </summary>
public readonly record struct CompositionGuide(bool IsVertical, double Position);

/// <summary>
/// Ce que le moteur doit tracer par-dessus son image : le cadre de chaque source composée
/// et, pour celle que l'opérateur a choisie, ses points d'accroche.
/// </summary>
/// <remarks>
/// Cadres et sélection voyagent ensemble : le thread graphique les relit à chaque image, et
/// deux champs échangés séparément lui donneraient une sélection qui ne correspond plus aux
/// cadres qu'elle accompagne. <see cref="IsCropping"/> voyage avec eux pour la même raison :
/// il dit sous quelle forme montrer la source choisie — poignées carrées pour l'étirer,
/// équerres et contour de la source entière pour la rogner. <see cref="Hovered"/> est la
/// source que le pointeur survole, montrée au poids de la sélection avant le clic ;
/// <see cref="Guides"/>, les lignes où se pose une source en cours de déplacement.
/// </remarks>
public sealed record CompositionOverlay(
    IReadOnlyList<OverlaySource> Sources,
    OverlaySource? Selected,
    bool IsCropping = false,
    OverlaySource? Hovered = null,
    IReadOnlyList<CompositionGuide>? Guides = null)
{
    public static CompositionOverlay Empty { get; } = new([], null);
}

/// <summary>
/// Composition d'une scène lue d'un seul tenant : la taille du canvas du moteur et la
/// transformation de chaque source, du premier plan vers l'arrière-plan.
/// </summary>
public sealed record SceneComposition(
    int CanvasWidth,
    int CanvasHeight,
    IReadOnlyList<SourceTransform> Sources)
{
    public static SceneComposition Empty { get; } = new(0, 0, []);
}
