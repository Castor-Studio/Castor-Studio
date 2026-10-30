using CastorApplication.Models.Studio;

namespace CastorApplication.ViewModels.Scenes;

/// <summary>
/// The eight handles of a source, in the order the engine paints them: clockwise from the
/// top-left corner (see <c>ObsPreviewGraphics.HandleRects</c>).
/// </summary>
public enum CompositionHandle
{
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left
}

/// <summary>
/// What a gesture on the canvas does to a source: move it, stretch it by a handle, crop it by
/// a handle, rotate it by a corner, slide its zoomed image under its frame, or aim at a point
/// of that image on its mini-map.
/// </summary>
public enum CompositionGestureKind
{
    Move,
    Resize,
    Crop,
    Rotate,
    Pan,
    Aim
}

/// <summary>
/// The placement a gesture requests from the engine. Everything starts from the transform
/// read at the start of the gesture and the pointer position since: a gesture recomputed from
/// its origin on every move does not drift, however many moves there are.
/// </summary>
/// <remarks>
/// A source turns around its point (X, Y), the top-left corner, as libobs composes it.
/// Stretching and cropping therefore count in the source's coordinates, along its own edges
/// rather than the canvas's, then return to the canvas by the same rotation.
/// <para>
/// Nothing is kept here: this is a proposal, which the engine confirms or refuses. Flipped
/// sources (negative scale) are not composed, so never grabbed.
/// </para>
/// </remarks>
public static class CompositionGeometry
{
    /// <summary>
    /// Smallest side, in canvas pixels, a stretch can reduce a source to: below it, its handles
    /// overlap and it can no longer be grabbed.
    /// </summary>
    public const double MinimumExtent = 8;

    /// <summary>Step of a constrained rotation, in degrees.</summary>
    public const double RotationStep = 15;

    // Corners first: on a source small enough for handles to overlap, the corner wins, since it
    // pulls both edges at once.
    private static readonly CompositionHandle[] HitOrder =
    [
        CompositionHandle.TopLeft,
        CompositionHandle.TopRight,
        CompositionHandle.BottomRight,
        CompositionHandle.BottomLeft,
        CompositionHandle.Top,
        CompositionHandle.Right,
        CompositionHandle.Bottom,
        CompositionHandle.Left
    ];

    private static readonly CompositionHandle[] Corners =
    [
        CompositionHandle.TopLeft,
        CompositionHandle.TopRight,
        CompositionHandle.BottomRight,
        CompositionHandle.BottomLeft
    ];

    /// <summary>
    /// This source's handle under this canvas point, within <paramref name="tolerance"/>, or none.
    /// </summary>
    public static CompositionHandle? HandleAt(SourceTransform source, double x, double y, double tolerance)
    {
        var (u, v) = ToLocal(source, x, y);
        foreach (var handle in HitOrder)
        {
            var (centerU, centerV) = HandlePoint(source, handle);
            if (Math.Abs(u - centerU) <= tolerance && Math.Abs(v - centerV) <= tolerance)
                return handle;
        }

        return null;
    }

    /// <summary>
    /// The corner whose rotation area this canvas point falls in: outside the source, within
    /// <paramref name="reach"/> of that corner. Inside the source, the same point moves it; that
    /// separates the two gestures without painting anything.
    /// </summary>
    public static CompositionHandle? RotationCornerAt(SourceTransform source, double x, double y, double reach)
    {
        if (Contains(source, x, y)) return null;

        var (u, v) = ToLocal(source, x, y);
        foreach (var corner in Corners)
        {
            var (cornerU, cornerV) = HandlePoint(source, corner);
            if (Math.Sqrt((u - cornerU) * (u - cornerU) + (v - cornerV) * (v - cornerV)) <= reach)
                return corner;
        }

        return null;
    }

    /// <summary>Whether this canvas point falls in the source's composed rectangle.</summary>
    public static bool Contains(SourceTransform source, double x, double y)
    {
        var (u, v) = ToLocal(source, x, y);
        return u >= 0 && u <= source.Width && v >= 0 && v <= source.Height;
    }

    /// <summary>The source's center in the canvas, around which it turns by hand.</summary>
    public static (double X, double Y) Center(SourceTransform source) =>
        ToCanvas(source, source.Width / 2, source.Height / 2);

    /// <summary>
    /// Snaps a moving source to the lines that matter: canvas edges and center, edges and centers
    /// of the other sources. On each axis, the source edge or center closest to a line, within
    /// <paramref name="reach"/>, lands on it.
    /// </summary>
    /// <returns>
    /// The snapped placement, and the lines the source now falls on: those are what the overlay
    /// draws as guides.
    /// </returns>
    /// <remarks>
    /// A rotated source snaps by its upright bounding rectangle: that is what the eye aligns. Only
    /// the position changes; a snap resizes nothing.
    /// </remarks>
    public static (SourcePlacement Placement, IReadOnlyList<CompositionGuide> Guides) Snap(
        SourceTransform start,
        SourcePlacement moved,
        IEnumerable<SourceTransform> others,
        double canvasWidth,
        double canvasHeight,
        double reach)
    {
        if (reach <= 0) return (moved, []);

        var verticals = new List<double>();
        var horizontals = new List<double>();
        if (canvasWidth > 0 && canvasHeight > 0)
        {
            verticals.AddRange([0, canvasWidth / 2, canvasWidth]);
            horizontals.AddRange([0, canvasHeight / 2, canvasHeight]);
        }

        foreach (var other in others)
        {
            if (other.SourceId == start.SourceId) continue;

            var (left, top, right, bottom) = Bounds(other);
            verticals.AddRange([left, (left + right) / 2, right]);
            horizontals.AddRange([top, (top + bottom) / 2, bottom]);
        }

        var (minX, minY, maxX, maxY) = Bounds(Preview(start, moved));
        double[] featuresX = [minX, (minX + maxX) / 2, maxX];
        double[] featuresY = [minY, (minY + maxY) / 2, maxY];
        var offsetX = Nearest(featuresX, verticals, reach);
        var offsetY = Nearest(featuresY, horizontals, reach);

        var guides = new List<CompositionGuide>();
        if (offsetX is { } x) AddGuides(guides, isVertical: true, featuresX, x, verticals);
        if (offsetY is { } y) AddGuides(guides, isVertical: false, featuresY, y, horizontals);

        return (moved with { X = moved.X + (offsetX ?? 0), Y = moved.Y + (offsetY ?? 0) }, guides);
    }

    /// <summary>
    /// The upright rectangle bounding the source in the canvas, rotation included: left, top,
    /// right, bottom.
    /// </summary>
    public static (double Left, double Top, double Right, double Bottom) Bounds(SourceTransform source)
    {
        var left = double.MaxValue;
        var top = double.MaxValue;
        var right = double.MinValue;
        var bottom = double.MinValue;
        foreach (var corner in Corners)
        {
            var (u, v) = HandlePoint(source, corner);
            var (x, y) = ToCanvas(source, u, v);
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }

        return (left, top, right, bottom);
    }

    /// <summary>
    /// The direction, in canvas degrees, this handle pulls in: 0 to the right, 90 down. It turns
    /// with the source: this is what picks the cursor.
    /// </summary>
    public static double PullDirection(CompositionHandle handle, double rotation)
    {
        var (sideX, sideY) = Sides(handle);
        return NormalizeDegrees(Math.Atan2(sideY, sideX) * 180 / Math.PI + rotation);
    }

    // The smallest move putting one of the source's markers on a line, within reach; none if
    // nothing is close enough.
    private static double? Nearest(double[] features, List<double> lines, double reach)
    {
        double? best = null;
        foreach (var feature in features)
        {
            foreach (var line in lines)
            {
                var offset = line - feature;
                if (Math.Abs(offset) <= reach && (best == null || Math.Abs(offset) < Math.Abs(best.Value)))
                    best = offset;
            }
        }

        return best;
    }

    // All the lines the source falls on once snapped: a center and an edge can align at the same
    // time, each deserves its guide.
    private static void AddGuides(
        List<CompositionGuide> guides,
        bool isVertical,
        double[] features,
        double offset,
        List<double> lines)
    {
        foreach (var line in lines)
        {
            if (guides.Exists(guide => guide.IsVertical == isVertical && Math.Abs(guide.Position - line) < 0.5))
                continue;

            foreach (var feature in features)
            {
                if (Math.Abs(feature + offset - line) >= 0.5) continue;

                guides.Add(new CompositionGuide(isVertical, line));
                break;
            }
        }
    }

    /// <summary>The source follows the pointer; scale, crop and rotation do not move.</summary>
    public static SourcePlacement Move(SourceTransform start, double deltaX, double deltaY) =>
        start.Placement with { X = start.X + deltaX, Y = start.Y + deltaY };

    /// <summary>
    /// The pulled handle takes its edge or edges with it, the opposite edge stays where it is. A
    /// corner keeps the source's proportions, unless <paramref name="keepAspectRatio"/> is false;
    /// a side only stretches its own axis.
    /// </summary>
    public static SourcePlacement Resize(
        SourceTransform start,
        CompositionHandle handle,
        double deltaX,
        double deltaY,
        bool keepAspectRatio)
    {
        if (start.Width <= 0 || start.Height <= 0) return start.Placement;

        var (sideX, sideY) = Sides(handle);
        var (deltaU, deltaV) = Turn(deltaX, deltaY, -start.Rotation);
        var width = Math.Max(MinimumExtent, start.Width + sideX * deltaU);
        var height = Math.Max(MinimumExtent, start.Height + sideY * deltaV);

        if (keepAspectRatio && sideX != 0 && sideY != 0)
        {
            // The most pulled edge decides: that is the one the operator is aiming at.
            var factor = Math.Max(width / start.Width, height / start.Height);
            factor = Math.Max(factor, MinimumExtent / Math.Min(start.Width, start.Height));
            width = start.Width * factor;
            height = start.Height * factor;
        }

        // The origin corner slides as far as the left or top edge advanced, in source coordinates:
        // that keeps the opposite edge in place, even when rotated.
        var (x, y) = ToCanvas(start,
            sideX < 0 ? start.Width - width : 0,
            sideY < 0 ? start.Height - height : 0);

        return start.Placement with
        {
            X = x,
            Y = y,
            ScaleX = start.ScaleX * width / start.Width,
            ScaleY = start.ScaleY * height / start.Height
        };
    }

    /// <summary>
    /// The pulled handle crops or gives back the image on its side, without changing the scale:
    /// the edge follows the pointer and the image stays in place under it. The crop never goes
    /// below zero and always leaves at least one source pixel.
    /// </summary>
    public static SourcePlacement Crop(
        SourceTransform start,
        CompositionHandle handle,
        double deltaX,
        double deltaY)
    {
        if (start.ScaleX <= 0 || start.ScaleY <= 0) return start.Placement;

        var (sideX, sideY) = Sides(handle);
        var (deltaU, deltaV) = Turn(deltaX, deltaY, -start.Rotation);
        var crop = start.Crop;

        // Crop counts in source pixels, before scaling.
        var sourceDeltaX = (int)Math.Round(deltaU / start.ScaleX);
        var sourceDeltaY = (int)Math.Round(deltaV / start.ScaleY);

        if (sideX < 0 && start.SourceWidth > 0)
            crop = crop with { Left = Math.Clamp(crop.Left + sourceDeltaX, 0, start.SourceWidth - crop.Right - 1) };
        else if (sideX > 0 && start.SourceWidth > 0)
            crop = crop with { Right = Math.Clamp(crop.Right - sourceDeltaX, 0, start.SourceWidth - crop.Left - 1) };

        if (sideY < 0 && start.SourceHeight > 0)
            crop = crop with { Top = Math.Clamp(crop.Top + sourceDeltaY, 0, start.SourceHeight - crop.Bottom - 1) };
        else if (sideY > 0 && start.SourceHeight > 0)
            crop = crop with { Bottom = Math.Clamp(crop.Bottom - sourceDeltaY, 0, start.SourceHeight - crop.Top - 1) };

        return WithCropOrigin(start, start.Placement with { Crop = crop });
    }

    /// <summary>
    /// Removes all crop without shifting what was visible: the source returns to its full size,
    /// the part already shown stays where it was.
    /// </summary>
    public static SourcePlacement ResetCrop(SourceTransform start) =>
        WithCropOrigin(start, start.Placement with { Crop = SourceCrop.None });

    /// <summary>
    /// The source turns around its center, by the angle the pointer traveled around that center
    /// since <paramref name="originX"/>, <paramref name="originY"/>.
    /// <paramref name="constrain"/> brings the angle to the <see cref="RotationStep"/> step.
    /// </summary>
    public static SourcePlacement Rotate(
        SourceTransform start,
        double originX,
        double originY,
        double x,
        double y,
        bool constrain)
    {
        var (centerX, centerY) = Center(start);
        var from = Math.Atan2(originY - centerY, originX - centerX);
        var to = Math.Atan2(y - centerY, x - centerX);
        var rotation = start.Rotation + (to - from) * 180 / Math.PI;
        if (constrain) rotation = Math.Round(rotation / RotationStep) * RotationStep;

        return TurnedTo(start, rotation);
    }

    /// <summary>The source turns by <paramref name="degrees"/> around its center.</summary>
    public static SourcePlacement RotateBy(SourceTransform start, double degrees) =>
        TurnedTo(start, start.Rotation + degrees);

    /// <summary>
    /// The source goes to the center of the canvas, rotation included: only its position changes.
    /// </summary>
    public static SourcePlacement CenterIn(SourceTransform start, double canvasWidth, double canvasHeight)
    {
        var (centerX, centerY) = Center(start);
        return Move(start, canvasWidth / 2 - centerX, canvasHeight / 2 - centerY);
    }

    /// <summary>
    /// The source takes the largest size at which it fits whole in the canvas, keeping its
    /// proportions, and goes to the center. A rotated source fits by its upright bounding
    /// rectangle: that is what has to fit in the picture.
    /// </summary>
    public static SourcePlacement Fit(SourceTransform start, double canvasWidth, double canvasHeight)
    {
        var (left, top, right, bottom) = Bounds(start);
        if (right - left <= 0 || bottom - top <= 0 || canvasWidth <= 0 || canvasHeight <= 0)
            return start.Placement;

        // The bounding rectangle grows with the scale: one factor is enough.
        var factor = Math.Min(canvasWidth / (right - left), canvasHeight / (bottom - top));
        var scaled = start.Placement with { ScaleX = start.ScaleX * factor, ScaleY = start.ScaleY * factor };
        return CenterIn(Preview(start, scaled), canvasWidth, canvasHeight);
    }

    /// <summary>
    /// The source goes back to the placement libobs gives it when added: in the top-left corner,
    /// at its own size, upright and uncropped.
    /// </summary>
    public static SourcePlacement Reset() => new(0, 0, 1, 1, SourceCrop.None);

    /// <summary>
    /// The zoomed image slides under the frame with the pointer, like a photo pushed with a
    /// finger: the aimed point goes the other way. The move counts along the source's edges, and
    /// the aimed point stops where the zoomed window touches the edge of the image.
    /// </summary>
    public static SourceZoom Pan(SourceTransform start, double deltaX, double deltaY)
    {
        var zoom = start.Zoom;
        if (!zoom.IsZoomed || start.Width <= 0 || start.Height <= 0) return zoom;

        var (u, v) = Turn(deltaX, deltaY, -start.Rotation);
        // The frame shows 1 / Factor of the image: crossing a whole frame moves it by that much. The
        // slide starts from the point the engine really shows, not an out-of-reach point.
        var shown = zoom.Aimed(zoom.CenterX, zoom.CenterY);
        return shown.Aimed(
            shown.CenterX - u / start.Width / zoom.Factor,
            shown.CenterY - v / start.Height / zoom.Factor);
    }

    /// <summary>
    /// The zoom goes to <paramref name="factor"/>, holding fixed the image point under
    /// (<paramref name="x"/>, <paramref name="y"/>) in the canvas, like a map zoomed towards the
    /// pointer. The aimed point stays where the zoomed window fits in the image.
    /// </summary>
    public static SourceZoom ZoomAt(SourceTransform start, double x, double y, double factor)
    {
        if (factor <= SourceZoom.MinFactor) return SourceZoom.None;
        if (start.Width <= 0 || start.Height <= 0) return start.Zoom with { Factor = factor };

        // Where the pointer falls in the frame, from 0 to 1 along its edges.
        var (u, v) = ToLocal(start, x, y);
        var frameX = Math.Clamp(u / start.Width, 0, 1);
        var frameY = Math.Clamp(v / start.Height, 0, 1);

        // The image point under the pointer is read in the previous window; the new one is placed
        // around it to keep it at the same spot of the frame.
        var before = start.Zoom.Aimed(start.Zoom.CenterX, start.Zoom.CenterY);
        var imageX = before.CenterX + (frameX - 0.5) / before.Factor;
        var imageY = before.CenterY + (frameY - 0.5) / before.Factor;
        return new SourceZoom(factor).Aimed(
            imageX - (frameX - 0.5) / factor,
            imageY - (frameY - 0.5) / factor);
    }

    /// <summary>An angle brought into ]-180, 180]: a full turn turns nothing.</summary>
    public static double NormalizeDegrees(double degrees)
    {
        var normalized = Math.IEEERemainder(degrees, 360);
        return normalized <= -180 ? normalized + 360 : normalized;
    }

    /// <summary>
    /// What the engine will compose for this placement, by its own rule (source size, minus the
    /// crop, scaled, then rotated). Used to show the gesture before the engine confirms it; its
    /// answer is what counts afterwards.
    /// </summary>
    public static SourceTransform Preview(SourceTransform start, SourcePlacement placement)
    {
        var croppedWidth = Math.Max(0, start.SourceWidth - placement.Crop.Left - placement.Crop.Right);
        var croppedHeight = Math.Max(0, start.SourceHeight - placement.Crop.Top - placement.Crop.Bottom);

        return start with
        {
            X = placement.X,
            Y = placement.Y,
            Width = croppedWidth * placement.ScaleX,
            Height = croppedHeight * placement.ScaleY,
            ScaleX = placement.ScaleX,
            ScaleY = placement.ScaleY,
            Crop = placement.Crop,
            Rotation = placement.Rotation
        };
    }

    /// <summary>This canvas point, in source coordinates: origin at point (X, Y).</summary>
    public static (double U, double V) ToLocal(SourceTransform source, double x, double y) =>
        Turn(x - source.X, y - source.Y, -source.Rotation);

    /// <summary>This source-coordinate point, in the canvas.</summary>
    public static (double X, double Y) ToCanvas(SourceTransform source, double u, double v)
    {
        var (x, y) = Turn(u, v, source.Rotation);
        return (source.X + x, source.Y + y);
    }

    // The origin point follows the left and top edges: cropping or giving back those edges moves
    // the origin by as much, in source coordinates, so the image stays in place under the moving
    // edge.
    private static SourcePlacement WithCropOrigin(SourceTransform start, SourcePlacement placement)
    {
        var (x, y) = ToCanvas(start,
            (placement.Crop.Left - start.Crop.Left) * start.ScaleX,
            (placement.Crop.Top - start.Crop.Top) * start.ScaleY);
        return placement with { X = x, Y = y };
    }

    // The center stays where it is: the origin is what turns around it.
    private static SourcePlacement TurnedTo(SourceTransform start, double rotation)
    {
        rotation = NormalizeDegrees(rotation);
        var (centerX, centerY) = Center(start);
        var (halfX, halfY) = Turn(start.Width / 2, start.Height / 2, rotation);
        return start.Placement with { X = centerX - halfX, Y = centerY - halfY, Rotation = rotation };
    }

    // A rotation in degrees in canvas coordinates, whose vertical axis points down: a positive
    // angle turns clockwise, as in libobs.
    private static (double X, double Y) Turn(double x, double y, double degrees)
    {
        if (degrees == 0) return (x, y);

        var radians = degrees * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return (x * cos - y * sin, x * sin + y * cos);
    }

    // Which edges a handle takes along: -1 the left or top edge, +1 the right or bottom edge,
    // 0 none on that axis.
    private static (int X, int Y) Sides(CompositionHandle handle) => handle switch
    {
        CompositionHandle.TopLeft => (-1, -1),
        CompositionHandle.Top => (0, -1),
        CompositionHandle.TopRight => (1, -1),
        CompositionHandle.Right => (1, 0),
        CompositionHandle.BottomRight => (1, 1),
        CompositionHandle.Bottom => (0, 1),
        CompositionHandle.BottomLeft => (-1, 1),
        _ => (-1, 0)
    };

    // A handle's point in source coordinates.
    private static (double U, double V) HandlePoint(SourceTransform source, CompositionHandle handle)
    {
        var (sideX, sideY) = Sides(handle);
        return (source.Width * (sideX + 1) / 2d, source.Height * (sideY + 1) / 2d);
    }
}
