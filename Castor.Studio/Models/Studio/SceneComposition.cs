using System.Globalization;

namespace CastorApplication.Models.Studio;

/// <summary>
/// Crop the engine applies to a source, in source pixels and before
/// scaling.
/// </summary>
public readonly record struct SourceCrop(int Left, int Top, int Right, int Bottom)
{
    public static SourceCrop None { get; } = new(0, 0, 0, 0);
}

/// <summary>
/// Transform a source undergoes in its scene's canvas, as the engine
/// holds it. <see cref="Width"/> and <see cref="Height"/> are the rectangle the engine
/// composes from crop and scale, <em>before</em> rotation: the source then turns
/// by <see cref="Rotation"/> degrees, clockwise, around
/// its point (<see cref="X"/>, <see cref="Y"/>) — the top-left corner, libobs's default
/// alignment.
/// </summary>
/// <remarks>
/// <see cref="Crop"/> is the operator's crop, the one that cuts the frame. The
/// <see cref="Zoom"/> happens inside this frame: the engine crops further and restores
/// the picture to the frame's size (its "bounds"), so that <see cref="Width"/>,
/// <see cref="Height"/> and the crop read here do not move when zooming.
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
    /// <summary>The picture's zoom inside its frame; <see cref="SourceZoom.None"/> without zoom.</summary>
    public SourceZoom Zoom { get; init; } = SourceZoom.None;

    /// <summary>What the engine holds of this transform, without what it derives from it.</summary>
    public SourcePlacement Placement => new(X, Y, ScaleX, ScaleY, Crop, Rotation);
}

/// <summary>
/// Placement requested from the engine for a source: position in the canvas, scale,
/// crop and rotation, in degrees. These are the only values it accepts; the composed
/// rectangle remains its own derivation and is read back from the <see cref="SourceTransform"/> it
/// confirms.
/// </summary>
public readonly record struct SourcePlacement(
    double X,
    double Y,
    double ScaleX,
    double ScaleY,
    SourceCrop Crop,
    double Rotation = 0);

/// <summary>
/// A source's color, that of its dot in the list, ready for the engine.
/// </summary>
public readonly record struct OverlayTint(float Red, float Green, float Blue)
{
    public static OverlayTint Default { get; } = new(0.357f, 0.553f, 0.937f);

    /// <summary>
    /// Reads a "#rrggbb" color. An unreadable color returns the default color: an
    /// outline without the exact color remains more useful than a source without an outline.
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
/// A source to draw: the rectangle the engine gives it, and the color under which
/// the interface names it elsewhere.
/// </summary>
public sealed record OverlaySource(SourceTransform Transform, OverlayTint Tint);

/// <summary>
/// An alignment line a moved source has just snapped to, drawn
/// from one edge of the canvas to the other: vertical at abscissa <see cref="Position"/>, or
/// horizontal at this ordinate.
/// </summary>
public readonly record struct CompositionGuide(bool IsVertical, double Position);

/// <summary>
/// What the engine must draw over its picture: the outline of each composed source
/// and, for the one the operator selected, its handles.
/// </summary>
/// <remarks>
/// Outlines and selection travel together: the graphics thread rereads them every frame, and
/// two fields swapped separately would give it a selection that no longer matches the
/// outlines it goes with. <see cref="IsCropping"/> travels with them for the same reason:
/// it says in which form to show the selected source — square handles to stretch it,
/// brackets and the whole source's outline to crop it. <see cref="Hovered"/> is the
/// source under the pointer, shown with the selection's weight before the click;
/// <see cref="Guides"/>, the lines where a source being moved settles.
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
/// A scene's composition read in one go: the size of the engine's canvas and the
/// transform of each source, from foreground to background.
/// </summary>
public sealed record SceneComposition(
    int CanvasWidth,
    int CanvasHeight,
    IReadOnlyList<SourceTransform> Sources)
{
    public static SceneComposition Empty { get; } = new(0, 0, []);
}
