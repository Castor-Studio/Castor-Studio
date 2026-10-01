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

    /// <summary>
    /// Accroche une source qu'on déplace aux lignes qui comptent : bords et centre du
    /// canvas, bords et centres des autres sources. Sur chaque axe, le bord ou le centre de
    /// la source le plus proche d'une ligne, à moins de <paramref name="reach"/>, s'y pose.
    /// </summary>
    /// <returns>
    /// Le placement accroché, et les lignes sur lesquelles la source tombe désormais : ce
    /// sont elles que l'overlay trace en guides.
    /// </returns>
    /// <remarks>
    /// Une source tournée s'accroche par le rectangle droit qui l'englobe : c'est ce que
    /// l'œil aligne. Seule la position change ; un aimant ne redimensionne rien.
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
    /// Le rectangle droit qui englobe la source dans le canvas, rotation comprise : gauche,
    /// haut, droite, bas.
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
    /// La direction, en degrés dans le canvas, dans laquelle tire cette poignée : 0 vers la
    /// droite, 90 vers le bas. Elle tourne avec la source — c'est ce qui choisit le curseur.
    /// </summary>
    public static double PullDirection(CompositionHandle handle, double rotation)
    {
        var (sideX, sideY) = Sides(handle);
        return NormalizeDegrees(Math.Atan2(sideY, sideX) * 180 / Math.PI + rotation);
    }

    // Le plus petit déplacement qui pose un des repères de la source sur une ligne, à
    // moins de reach ; aucun si rien n'est assez près.
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

    // Toutes les lignes sur lesquelles la source tombe une fois accrochée : un centre et un
    // bord peuvent s'aligner en même temps, chacun mérite son guide.
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
