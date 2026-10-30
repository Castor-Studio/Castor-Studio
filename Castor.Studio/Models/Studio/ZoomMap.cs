namespace CastorApplication.Models.Studio;

/// <summary>
/// A rectangle in a frame's coordinates: origin at its top-left corner, axes turned
/// with it.
/// </summary>
public readonly record struct FrameRect(double X, double Y, double Width, double Height)
{
    public bool Contains(double x, double y) => x >= X && x <= X + Width && y >= Y && y <= Y + Height;
}

/// <summary>
/// A zoomed source's mini-map: the whole picture in small, in the bottom-right corner
/// of its frame, with the window the frame shows. The engine paints it; the interface
/// reads clicks on it. Both go through here to land on the same pixel.
/// </summary>
public static class ZoomMap
{
    // Thought of in screen pixels: the map is read and aimed with the mouse, whatever
    // the scale at which the canvas is reduced in the panel.
    private const double TargetWidth = 120;
    private const double MinimumWidth = 40;
    private const double Margin = 8;

    // The map never takes more than this share of the frame: the picture is what counts.
    private const double MaximumShare = 0.35;

    /// <summary>
    /// Where the map sits in a frame of <paramref name="frameWidth"/> ×
    /// <paramref name="frameHeight"/>, in canvas pixels, when one screen pixel is
    /// <paramref name="canvasPerScreenPixel"/> canvas pixels. Nothing if the frame is too
    /// small for it to be read.
    /// </summary>
    public static FrameRect? Bounds(double frameWidth, double frameHeight, double canvasPerScreenPixel)
    {
        if (frameWidth <= 0 || frameHeight <= 0 || canvasPerScreenPixel <= 0) return null;

        // The map has the frame's shape: it is the picture as the frame shows it, in small.
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
    /// The window the frame shows of the picture, in fractions of the whole picture (0 to 1),
    /// held against the edge as the engine holds it.
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
    /// The zoom that centers the window on this point of the map, given in frame coordinates.
    /// A point outside the map aims at the nearest edge.
    /// </summary>
    public static SourceZoom Aim(SourceZoom zoom, FrameRect map, double x, double y)
    {
        if (!zoom.IsZoomed || map.Width <= 0 || map.Height <= 0) return zoom;

        return zoom.Aimed((x - map.X) / map.Width, (y - map.Y) / map.Height);
    }
}
