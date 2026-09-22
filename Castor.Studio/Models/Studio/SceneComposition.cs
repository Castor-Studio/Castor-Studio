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
/// compose à partir du rognage et de l'échelle : l'interface les pose tels quels, elle n'a
/// aucune géométrie à en déduire.
/// </summary>
/// <remarks>
/// libobs connaît aussi des « bounds » (un cadre qui contraint la taille rendue). Le binding
/// LibObs ne les expose pas encore ; quand ils arriveront, ils changeront le rectangle rendu
/// ici même, et rien dans l'interface.
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
    bool IsVisible);

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
/// Ce que le moteur doit tracer par-dessus son image : le cadre de chaque source composée
/// et, pour celle que l'opérateur a choisie, ses points d'accroche.
/// </summary>
/// <remarks>
/// Cadres et sélection voyagent ensemble : le thread graphique les relit à chaque image, et
/// deux champs échangés séparément lui donneraient une sélection qui ne correspond plus aux
/// cadres qu'elle accompagne.
/// </remarks>
public sealed record CompositionOverlay(
    IReadOnlyList<OverlaySource> Sources,
    OverlaySource? Selected)
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
