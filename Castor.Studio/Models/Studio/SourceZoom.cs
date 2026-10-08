namespace CastorApplication.Models.Studio;

/// <summary>
/// Zoom numérique d'une source à l'intérieur de son cadre : le cadre reste où il est et garde
/// sa taille, seule l'image qu'il montre s'agrandit. <see cref="Factor"/> vaut 1 pour l'image
/// entière. (<see cref="CenterX"/>, <see cref="CenterY"/>) est le point visé, entre 0 et 1 dans
/// la partie de la source que le rognage laisse visible : il vient au centre du cadre, tant que
/// la fenêtre zoomée ne sort pas de l'image.
/// </summary>
public readonly record struct SourceZoom(double Factor, double CenterX = 0.5, double CenterY = 0.5)
{
    public const double MinFactor = 1;
    public const double MaxFactor = 8;

    public static SourceZoom None { get; } = new(MinFactor);

    /// <summary>Si l'image est réellement agrandie.</summary>
    public bool IsZoomed => Factor > MinFactor;

    /// <summary>
    /// Ce zoom, visant (<paramref name="centerX"/>, <paramref name="centerY"/>) : le point est
    /// ramené là où la fenêtre zoomée tient entière dans l'image, comme le moteur la cale.
    /// </summary>
    public SourceZoom Aimed(double centerX, double centerY)
    {
        var half = 0.5 / Factor;
        return this with
        {
            CenterX = Math.Clamp(centerX, half, 1 - half),
            CenterY = Math.Clamp(centerY, half, 1 - half)
        };
    }

    /// <summary>
    /// Rognage que ce zoom ajoute dans une image visible de <paramref name="width"/> ×
    /// <paramref name="height"/> pixels : ce qu'il faut retirer de chaque bord pour n'en garder
    /// que la fenêtre zoomée. La fenêtre est poussée contre le bord plutôt que d'en sortir.
    /// </summary>
    public SourceCrop CropWithin(int width, int height)
    {
        if (!IsZoomed || width <= 0 || height <= 0) return SourceCrop.None;

        var windowWidth = Math.Clamp((int)Math.Round(width / Factor), 1, width);
        var windowHeight = Math.Clamp((int)Math.Round(height / Factor), 1, height);
        var left = Offset(CenterX, width, windowWidth);
        var top = Offset(CenterY, height, windowHeight);
        return new SourceCrop(left, top, width - windowWidth - left, height - windowHeight - top);
    }

    private static int Offset(double center, int size, int window) =>
        Math.Clamp((int)Math.Round(center * size - window / 2.0), 0, size - window);
}
