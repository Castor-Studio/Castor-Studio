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
/// ou la rogner par une poignée.
/// </summary>
public enum CompositionGestureKind
{
    Move,
    Resize,
    Crop
}

/// <summary>
/// Le placement qu'un geste demande au moteur. Tout part de la transformation lue au
/// début du geste et du déplacement total du pointeur depuis : un geste recalculé depuis
/// son origine à chaque mouvement ne dérive pas, quel que soit le nombre de mouvements.
/// </summary>
/// <remarks>
/// Rien ici n'est retenu : c'est une proposition, que le moteur confirme ou refuse. Les
/// sources retournées (échelle négative) ne sont pas composées, donc jamais saisies.
/// </remarks>
public static class CompositionGeometry
{
    /// <summary>
    /// Plus petit côté, en pixels du canvas, auquel un étirement peut réduire une source :
    /// en dessous, ses poignées se recouvrent et elle ne se saisit plus.
    /// </summary>
    public const double MinimumExtent = 8;

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

    /// <summary>
    /// La poignée de cette source sous ce point du canvas, à <paramref name="tolerance"/>
    /// près, ou aucune.
    /// </summary>
    public static CompositionHandle? HandleAt(SourceTransform source, double x, double y, double tolerance)
    {
        foreach (var handle in HitOrder)
        {
            var (centerX, centerY) = HandleCenter(source, handle);
            if (Math.Abs(x - centerX) <= tolerance && Math.Abs(y - centerY) <= tolerance)
                return handle;
        }

        return null;
    }

    /// <summary>Si ce point du canvas tombe dans le rectangle composé de la source.</summary>
    public static bool Contains(SourceTransform source, double x, double y) =>
        x >= source.X && x <= source.X + source.Width &&
        y >= source.Y && y <= source.Y + source.Height;

    /// <summary>La source suit le pointeur ; échelle et rognage ne bougent pas.</summary>
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
        var width = Math.Max(MinimumExtent, start.Width + sideX * deltaX);
        var height = Math.Max(MinimumExtent, start.Height + sideY * deltaY);

        if (keepAspectRatio && sideX != 0 && sideY != 0)
        {
            // Le bord le plus tiré décide : c'est lui que l'opérateur est en train de viser.
            var factor = Math.Max(width / start.Width, height / start.Height);
            factor = Math.Max(factor, MinimumExtent / Math.Min(start.Width, start.Height));
            width = start.Width * factor;
            height = start.Height * factor;
        }

        return start.Placement with
        {
            X = sideX < 0 ? start.X + start.Width - width : start.X,
            Y = sideY < 0 ? start.Y + start.Height - height : start.Y,
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
        var crop = start.Crop;
        var x = start.X;
        var y = start.Y;

        // Le rognage se compte en pixels de la source, avant mise à l'échelle.
        var sourceDeltaX = (int)Math.Round(deltaX / start.ScaleX);
        var sourceDeltaY = (int)Math.Round(deltaY / start.ScaleY);

        if (sideX < 0 && start.SourceWidth > 0)
        {
            var left = Math.Clamp(crop.Left + sourceDeltaX, 0, start.SourceWidth - crop.Right - 1);
            x += (left - crop.Left) * start.ScaleX;
            crop = crop with { Left = left };
        }
        else if (sideX > 0 && start.SourceWidth > 0)
        {
            crop = crop with { Right = Math.Clamp(crop.Right - sourceDeltaX, 0, start.SourceWidth - crop.Left - 1) };
        }

        if (sideY < 0 && start.SourceHeight > 0)
        {
            var top = Math.Clamp(crop.Top + sourceDeltaY, 0, start.SourceHeight - crop.Bottom - 1);
            y += (top - crop.Top) * start.ScaleY;
            crop = crop with { Top = top };
        }
        else if (sideY > 0 && start.SourceHeight > 0)
        {
            crop = crop with { Bottom = Math.Clamp(crop.Bottom - sourceDeltaY, 0, start.SourceHeight - crop.Top - 1) };
        }

        return start.Placement with { X = x, Y = y, Crop = crop };
    }

    /// <summary>
    /// Ce que le moteur composera pour ce placement, à la même règle que lui (taille de la
    /// source, moins le rognage, mise à l'échelle). Sert à montrer le geste avant que le
    /// moteur ne l'ait confirmé ; c'est sa réponse, ensuite, qui fait foi.
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
            Crop = placement.Crop
        };
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

    private static (double X, double Y) HandleCenter(SourceTransform source, CompositionHandle handle)
    {
        var (sideX, sideY) = Sides(handle);
        return (
            source.X + source.Width * (sideX + 1) / 2d,
            source.Y + source.Height * (sideY + 1) / 2d);
    }
}
