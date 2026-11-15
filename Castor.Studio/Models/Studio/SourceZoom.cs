namespace CastorApplication.Models.Studio;

/// <summary>
/// Digital zoom of a source inside its frame: the frame stays where it is and keeps its size,
/// only the image it shows gets larger. <see cref="Factor"/> is 1 for the full image.
/// (<see cref="CenterX"/>, <see cref="CenterY"/>) is the aimed point, between 0 and 1 in the
/// part of the source the crop leaves visible: it comes to the center of the frame, as long
/// as the zoomed window stays inside the image.
/// </summary>
public readonly record struct SourceZoom(double Factor, double CenterX = 0.5, double CenterY = 0.5)
{
    public const double MinFactor = 1;
    public const double MaxFactor = 8;

    public static SourceZoom None { get; } = new(MinFactor);

    /// <summary>Whether the image is really enlarged.</summary>
    public bool IsZoomed => Factor > MinFactor;

    /// <summary>
    /// This zoom, aimed at (<paramref name="centerX"/>, <paramref name="centerY"/>): the point is
    /// brought to where the zoomed window fits whole in the image, as the engine pins it.
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
    /// Crop this zoom adds in a visible image of <paramref name="width"/> ×
    /// <paramref name="height"/> pixels: what to remove from each edge to keep only the zoomed
    /// window. The window is pushed against the edge rather than leaving the image.
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
