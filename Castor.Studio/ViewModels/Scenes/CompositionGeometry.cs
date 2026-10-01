using CastorApplication.Models.Studio;

namespace CastorApplication.ViewModels.Scenes;

/// <summary>
/// Les huit poignées d'une source, dans l'ordre où le moteur les peint : dans le sens des
/// aiguilles d'une montre depuis le coin haut-gauche (voir
/// <c>ObsPreviewGraphics.HandleRects</c>).
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
/// Ce qu'un geste sur le canvas fait à une source : la déplacer, l'étirer par une poignée,
/// la rogner par une poignée, ou la faire tourner par un de ses coins.
/// </summary>
public enum CompositionGestureKind
{
    Move,
    Resize,
    Crop,
    Rotate
}

/// <summary>
/// Le placement qu'un geste demande au moteur. Tout part de la transformation lue au
/// début du geste et de la position du pointeur depuis : un geste recalculé depuis son
/// origine à chaque mouvement ne dérive pas, quel que soit le nombre de mouvements.
/// </summary>
/// <remarks>
/// Une source tourne autour de son point (X, Y), le coin haut-gauche, comme libobs la
/// compose. Étirer et rogner se comptent donc dans le repère de la source — le long de ses
/// propres bords, pas de ceux du canvas — puis reviennent au canvas par la même rotation.
/// <para>
/// Rien ici n'est retenu : c'est une proposition, que le moteur confirme ou refuse. Les
/// sources retournées (échelle négative) ne sont pas composées, donc jamais saisies.
/// </para>
/// </remarks>
public static class CompositionGeometry
{
    /// <summary>
    /// Plus petit côté, en pixels du canvas, auquel un étirement peut réduire une source :
    /// en dessous, ses poignées se recouvrent et elle ne se saisit plus.
    /// </summary>
    public const double MinimumExtent = 8;

    /// <summary>Pas d'une rotation contrainte, en degrés.</summary>
    public const double RotationStep = 15;

    // Les angles d'abord : sur une source assez petite pour que les poignées se chevauchent,
    // c'est l'angle qui l'emporte, puisqu'il tire les deux bords à la fois.
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
    /// La poignée de cette source sous ce point du canvas, à <paramref name="tolerance"/>
    /// près, ou aucune.
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
    /// Le coin dont ce point du canvas tombe dans la zone de rotation : à l'extérieur de la
    /// source, à moins de <paramref name="reach"/> de ce coin. À l'intérieur de la source,
    /// le même point la déplace ; c'est ce qui sépare les deux gestes sans rien peindre.
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

    /// <summary>Si ce point du canvas tombe dans le rectangle composé de la source.</summary>
    public static bool Contains(SourceTransform source, double x, double y)
    {
        var (u, v) = ToLocal(source, x, y);
        return u >= 0 && u <= source.Width && v >= 0 && v <= source.Height;
    }

    /// <summary>Le centre de la source dans le canvas, autour duquel elle tourne à la main.</summary>
    public static (double X, double Y) Center(SourceTransform source) =>
        ToCanvas(source, source.Width / 2, source.Height / 2);

    /// <summary>La source suit le pointeur ; échelle, rognage et rotation ne bougent pas.</summary>
    public static SourcePlacement Move(SourceTransform start, double deltaX, double deltaY) =>
        start.Placement with { X = start.X + deltaX, Y = start.Y + deltaY };

    /// <summary>
    /// La poignée tirée emmène son ou ses bords, le bord opposé reste où il est. Un angle
    /// garde les proportions de la source, sauf si <paramref name="keepAspectRatio"/> est
    /// faux ; un côté n'étire que son axe.
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
            // Le bord le plus tiré décide : c'est lui que l'opérateur est en train de viser.
            var factor = Math.Max(width / start.Width, height / start.Height);
            factor = Math.Max(factor, MinimumExtent / Math.Min(start.Width, start.Height));
            width = start.Width * factor;
            height = start.Height * factor;
        }

        // Le coin d'origine glisse d'autant que le bord gauche ou haut a avancé, dans le
        // repère de la source : c'est ce qui laisse le bord opposé en place, même tournée.
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
    /// La poignée tirée rogne ou rend l'image de son côté, sans changer l'échelle : le bord
    /// suit le pointeur et l'image reste en place sous lui. Le rognage ne descend jamais sous
    /// zéro et laisse toujours au moins un pixel de la source.
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

        // Le rognage se compte en pixels de la source, avant mise à l'échelle.
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
    /// Ôte tout rognage, sans rien décaler de ce qui était visible : la source reprend sa
    /// taille entière, la partie déjà montrée reste où elle était.
    /// </summary>
    public static SourcePlacement ResetCrop(SourceTransform start) =>
        WithCropOrigin(start, start.Placement with { Crop = SourceCrop.None });

    /// <summary>
    /// La source tourne autour de son centre, de l'angle que le pointeur a parcouru autour
    /// de ce centre depuis <paramref name="originX"/>, <paramref name="originY"/>.
    /// <paramref name="constrain"/> ramène l'angle au pas de <see cref="RotationStep"/>.
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

    /// <summary>La source tourne de <paramref name="degrees"/> autour de son centre.</summary>
    public static SourcePlacement RotateBy(SourceTransform start, double degrees) =>
        TurnedTo(start, start.Rotation + degrees);

    /// <summary>Un angle ramené dans ]-180, 180] : un tour complet ne tourne rien.</summary>
    public static double NormalizeDegrees(double degrees)
    {
        var normalized = Math.IEEERemainder(degrees, 360);
        return normalized <= -180 ? normalized + 360 : normalized;
    }

    /// <summary>
    /// Ce que le moteur composera pour ce placement, à la même règle que lui (taille de la
    /// source, moins le rognage, mise à l'échelle, puis rotation). Sert à montrer le geste
    /// avant que le moteur ne l'ait confirmé ; c'est sa réponse, ensuite, qui fait foi.
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

    /// <summary>Ce point du canvas, dans le repère de la source : origine au point (X, Y).</summary>
    public static (double U, double V) ToLocal(SourceTransform source, double x, double y) =>
        Turn(x - source.X, y - source.Y, -source.Rotation);

    /// <summary>Ce point du repère de la source, dans le canvas.</summary>
    public static (double X, double Y) ToCanvas(SourceTransform source, double u, double v)
    {
        var (x, y) = Turn(u, v, source.Rotation);
        return (source.X + x, source.Y + y);
    }

    // Le point d'origine suit le bord gauche et le bord haut : rogner ou rendre ces bords
    // déplace l'origine d'autant, dans le repère de la source, pour que l'image reste en
    // place sous le bord qui bouge.
    private static SourcePlacement WithCropOrigin(SourceTransform start, SourcePlacement placement)
    {
        var (x, y) = ToCanvas(start,
            (placement.Crop.Left - start.Crop.Left) * start.ScaleX,
            (placement.Crop.Top - start.Crop.Top) * start.ScaleY);
        return placement with { X = x, Y = y };
    }

    // Le centre reste où il est : c'est l'origine qui tourne autour de lui.
    private static SourcePlacement TurnedTo(SourceTransform start, double rotation)
    {
        rotation = NormalizeDegrees(rotation);
        var (centerX, centerY) = Center(start);
        var (halfX, halfY) = Turn(start.Width / 2, start.Height / 2, rotation);
        return start.Placement with { X = centerX - halfX, Y = centerY - halfY, Rotation = rotation };
    }

    // Une rotation de degrés dans le repère du canvas, dont l'axe vertical descend : un angle
    // positif tourne dans le sens des aiguilles d'une montre, comme dans libobs.
    private static (double X, double Y) Turn(double x, double y, double degrees)
    {
        if (degrees == 0) return (x, y);

        var radians = degrees * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return (x * cos - y * sin, x * sin + y * cos);
    }

    // Quels bords une poignée emmène : -1 le bord gauche ou haut, +1 le bord droit ou bas,
    // 0 aucun sur cet axe.
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

    // Le point d'une poignée dans le repère de la source.
    private static (double U, double V) HandlePoint(SourceTransform source, CompositionHandle handle)
    {
        var (sideX, sideY) = Sides(handle);
        return (source.Width * (sideX + 1) / 2d, source.Height * (sideY + 1) / 2d);
    }
}
