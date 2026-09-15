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
