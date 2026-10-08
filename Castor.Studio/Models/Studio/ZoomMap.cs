namespace CastorApplication.Models.Studio;

/// <summary>
/// Un rectangle dans le repère d'un cadre : origine en son coin haut-gauche, axes tournés
/// avec lui.
/// </summary>
public readonly record struct FrameRect(double X, double Y, double Width, double Height)
{
    public bool Contains(double x, double y) => x >= X && x <= X + Width && y >= Y && y <= Y + Height;
}

/// <summary>
/// La mini-carte d'une source zoomée : l'image entière en réduction, dans le coin bas-droit
/// de son cadre, avec la fenêtre que le cadre en montre. Le moteur la peint ; l'interface y
/// lit les clics. Les deux passent par ici pour tomber au même pixel.
/// </summary>
public static class ZoomMap
{
    // Pensées en pixels de l'écran : la carte se lit et se vise à la souris, quelle que
    // soit l'échelle à laquelle le canvas est réduit dans le panneau.
    private const double TargetWidth = 120;
    private const double MinimumWidth = 40;
    private const double Margin = 8;

    // La carte ne mange jamais plus que cette part du cadre : c'est l'image qui compte.
    private const double MaximumShare = 0.35;

    /// <summary>
    /// Où se pose la carte dans un cadre de <paramref name="frameWidth"/> ×
    /// <paramref name="frameHeight"/>, en pixels du canvas, quand un pixel de l'écran vaut
    /// <paramref name="canvasPerScreenPixel"/> pixels du canvas. Rien si le cadre est trop
    /// petit pour qu'elle se lise.
    /// </summary>
    public static FrameRect? Bounds(double frameWidth, double frameHeight, double canvasPerScreenPixel)
    {
        if (frameWidth <= 0 || frameHeight <= 0 || canvasPerScreenPixel <= 0) return null;

        // La carte a la forme du cadre : c'est l'image telle qu'il la montre, en petit.
        var aspect = frameHeight / frameWidth;
        var width = Math.Min(
            TargetWidth * canvasPerScreenPixel,
            Math.Min(frameWidth * MaximumShare, frameHeight * MaximumShare / aspect));
        if (width < MinimumWidth * canvasPerScreenPixel) return null;

        var height = width * aspect;
        var margin = Margin * canvasPerScreenPixel;
        return new FrameRect(frameWidth - width - margin, frameHeight - height - margin, width, height);
    }

    /// <summary>
    /// La fenêtre que le cadre montre de l'image, en fractions de l'image entière (de 0 à 1),
    /// calée contre le bord comme le moteur la cale.
    /// </summary>
    public static FrameRect Window(SourceZoom zoom)
    {
        if (!zoom.IsZoomed) return new FrameRect(0, 0, 1, 1);

        var size = 1 / zoom.Factor;
        return new FrameRect(
            Math.Clamp(zoom.CenterX - size / 2, 0, 1 - size),
            Math.Clamp(zoom.CenterY - size / 2, 0, 1 - size),
            size,
            size);
    }

    /// <summary>
    /// Le zoom qui centre la fenêtre sur ce point de la carte, donné dans le repère du cadre.
    /// Un point hors de la carte vise le bord le plus proche.
    /// </summary>
    public static SourceZoom Aim(SourceZoom zoom, FrameRect map, double x, double y)
    {
        if (!zoom.IsZoomed || map.Width <= 0 || map.Height <= 0) return zoom;

        return zoom.Aimed((x - map.X) / map.Width, (y - map.Y) / map.Height);
    }
}
