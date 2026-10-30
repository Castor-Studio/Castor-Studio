using System.Globalization;
using System.Runtime.InteropServices;
using CastorApplication.Models.Studio;
using LibObs;

namespace CastorApplication.Services.Studio;

internal readonly record struct PreviewViewport(int X, int Y, int Width, int Height);

/// <summary>
/// Un rectangle plein à peindre, dans le repère d'une source : origine en son point (X, Y),
/// axes tournés avec elle.
/// </summary>
internal readonly record struct PreviewFillRect(float X, float Y, float Width, float Height);

/// <summary>
/// Les tailles de l'overlay, en pixels du canvas, pour l'image en cours. Elles sont pensées
/// en pixels de l'écran puis converties : une poignée se vise à la souris, elle ne suit pas
/// l'échelle à laquelle le canvas est réduit dans le panneau.
/// </summary>
internal readonly record struct OverlayMetrics(
    float IdleThickness,
    float ChosenThickness,
    float Handle,
    float HandleCore,
    float Shadow,
    float CropArm,
    float CropThickness,
    float Dash,
    float BadgeInset = 6,
    float CanvasPerScreenPixel = 1);

internal static class ObsPreviewGraphics
{
    private const string ObsLibrary = "obs";

    // obs_base_effect : l'effet « solid » est le quatrième de l'énumération de libobs.
    private const int ObsEffectSolid = 3;

    // obs_base_effect : l'effet par défaut, qui peint une texture, est le premier.
    private const int ObsEffectDefault = 0;

    // gs_color_format : GS_BGRA, l'ordre des pixels que rend Avalonia.
    private const int GsColorFormatBgra = 5;

    // Le cadre d'une source porte la couleur de sa pastille dans la liste : sur une
    // composition qui se chevauche, c'est ce qui dit quel cadre est quelle ligne. Ce qui
    // reste vient de Styles/Colors.axaml, dans les valeurs du thème sombre : la zone
    // d'aperçu est noire en clair comme en sombre.
    private static readonly Vec4 Chosen = new(0.357f, 0.553f, 0.937f, 1f);     // AppAccentFg
    private static readonly Vec4 HandleCore = new(0.918f, 0.918f, 0.941f, 1f); // AppFg1
    private static readonly Vec4 Shadow = new(0.043f, 0.043f, 0.071f, 1f);     // AppBg
    private static readonly Vec4 Plate = new(0.043f, 0.043f, 0.071f, 0.78f);   // AppBg, voilé
    private static readonly Vec4 ChosenVeil = new(0.357f, 0.553f, 0.937f, 0.3f); // AppAccentFg, voilé

    // Un symbole graphique absent ne doit pas emporter le thread de rendu de libobs : au
    // premier échec on renonce à l'overlay pour de bon, l'image, elle, continue.
    private static volatile bool _outlinesUnavailable;

    internal static PreviewViewport CalculateViewport(
        uint displayWidth,
        uint displayHeight,
        uint canvasWidth,
        uint canvasHeight)
    {
        if (displayWidth == 0 || displayHeight == 0 || canvasWidth == 0 || canvasHeight == 0)
            return new PreviewViewport(0, 0, 0, 0);

        var displayAspect = (double)displayWidth / displayHeight;
        var canvasAspect = (double)canvasWidth / canvasHeight;
        int width;
        int height;
        if (displayAspect > canvasAspect)
        {
            width = Math.Max(1, (int)(displayHeight * canvasAspect));
            height = (int)displayHeight;
        }
        else
        {
            width = (int)displayWidth;
            height = Math.Max(1, (int)(displayWidth / canvasAspect));
        }

        var x = (int)displayWidth / 2 - width / 2;
        var y = (int)displayHeight / 2 - height / 2;
        return new PreviewViewport(x, y, width, height);
    }

    internal static OverlayMetrics MetricsFor(int viewportWidth, uint canvasWidth) => new(
        IdleThickness: ToCanvasPixels(1f, viewportWidth, canvasWidth, minimum: 1f),
        ChosenThickness: ToCanvasPixels(2f, viewportWidth, canvasWidth, minimum: 1f),
        Handle: ToCanvasPixels(10f, viewportWidth, canvasWidth, minimum: 4f),
        HandleCore: ToCanvasPixels(5f, viewportWidth, canvasWidth, minimum: 2f),
        Shadow: ToCanvasPixels(1f, viewportWidth, canvasWidth, minimum: 1f),
        CropArm: ToCanvasPixels(16f, viewportWidth, canvasWidth, minimum: 6f),
        CropThickness: ToCanvasPixels(4f, viewportWidth, canvasWidth, minimum: 2f),
        Dash: ToCanvasPixels(6f, viewportWidth, canvasWidth, minimum: 2f),
        BadgeInset: ToCanvasPixels(6f, viewportWidth, canvasWidth, minimum: 2f),
        CanvasPerScreenPixel: ToCanvasPixels(1f, viewportWidth, canvasWidth, minimum: 0f));

    private static float ToCanvasPixels(float devicePixels, int viewportWidth, uint canvasWidth, float minimum)
    {
        if (viewportWidth <= 0 || canvasWidth == 0) return devicePixels;

        return Math.Max(minimum, devicePixels * canvasWidth / viewportWidth);
    }

    /// <summary>
    /// Les quatre bords d'un cadre, tracés <em>à l'intérieur</em> du rectangle de la source :
    /// un cadre posé à cheval sur le bord ferait paraître la source plus grande qu'elle
    /// n'est, alors que c'est justement sa taille réelle qu'il montre.
    /// </summary>
    internal static IReadOnlyList<PreviewFillRect> BoxEdges(
        double x,
        double y,
        double width,
        double height,
        float thickness)
    {
        if (width <= 0 || height <= 0 || thickness <= 0) return [];

        var left = (float)x;
        var top = (float)y;
        var boxWidth = (float)width;
        var boxHeight = (float)height;

        // Une source plus fine que deux traits est peinte pleine : deux bords qui se
        // recouvrent dessineraient un cadre plus épais que la source elle-même.
        if (boxWidth <= thickness * 2 || boxHeight <= thickness * 2)
            return [new PreviewFillRect(left, top, boxWidth, boxHeight)];

        var innerHeight = boxHeight - thickness * 2;
        return
        [
            new PreviewFillRect(left, top, boxWidth, thickness),
            new PreviewFillRect(left, top + boxHeight - thickness, boxWidth, thickness),
            new PreviewFillRect(left, top + thickness, thickness, innerHeight),
            new PreviewFillRect(left + boxWidth - thickness, top + thickness, thickness, innerHeight)
        ];
    }

    /// <summary>
    /// Les huit poignées d'une source : quatre aux angles, quatre au milieu des côtés.
    /// Chacune est centrée sur son point, donc à cheval sur le bord — c'est ce qui la rend
    /// saisissable des deux côtés du trait, et ce qui la laisse visible sur une source
    /// collée au bord du canvas.
    /// </summary>
    /// <remarks>
    /// Rendues dans le sens des aiguilles d'une montre depuis le coin haut-gauche : c'est
    /// l'ordre dont le geste d'étirement se servira pour savoir quel bord il tire.
    /// </remarks>
    internal static IReadOnlyList<PreviewFillRect> HandleRects(
        double x,
        double y,
        double width,
        double height,
        float size)
    {
        if (width <= 0 || height <= 0 || size <= 0) return [];

        var left = (float)x;
        var top = (float)y;
        var right = (float)(x + width);
        var bottom = (float)(y + height);
        var middleX = (float)(x + width / 2);
        var middleY = (float)(y + height / 2);

        return
        [
            Handle(left, top, size),
            Handle(middleX, top, size),
            Handle(right, top, size),
            Handle(right, middleY, size),
            Handle(right, bottom, size),
            Handle(middleX, bottom, size),
            Handle(left, bottom, size),
            Handle(left, middleY, size)
        ];
    }

    private static PreviewFillRect Handle(float centerX, float centerY, float size) =>
        new(centerX - size / 2f, centerY - size / 2f, size, size);

    /// <summary>
    /// Les poignées du rognage : une équerre à chaque angle, une barre au milieu de chaque
    /// côté, posées <em>à l'intérieur</em> du bord. Une autre forme que les carrés de
    /// l'étirement, parce que le même geste n'y fait pas la même chose : on ne tire plus la
    /// source, on en retire des bords.
    /// </summary>
    /// <remarks>
    /// Huit poignées dans le même ordre que <see cref="HandleRects"/> — chacune en un ou deux
    /// rectangles —, sur un rectangle d'origine (0, 0).
    /// </remarks>
    internal static IReadOnlyList<PreviewFillRect> CropBrackets(
        double width,
        double height,
        float arm,
        float thickness)
    {
        if (width <= 0 || height <= 0 || arm <= 0 || thickness <= 0) return [];

        var right = (float)width;
        var bottom = (float)height;
        // Une source plus petite que deux équerres garde des bras qui ne se croisent pas.
        var armX = Math.Min(arm, right / 3f);
        var armY = Math.Min(arm, bottom / 3f);
        var middleX = right / 2f;
        var middleY = bottom / 2f;

        return
        [
            new(0, 0, armX, thickness), new(0, 0, thickness, armY),
            new(middleX - armX / 2f, 0, armX, thickness),
            new(right - armX, 0, armX, thickness), new(right - thickness, 0, thickness, armY),
            new(right - thickness, middleY - armY / 2f, thickness, armY),
            new(right - armX, bottom - thickness, armX, thickness), new(right - thickness, bottom - armY, thickness, armY),
            new(middleX - armX / 2f, bottom - thickness, armX, thickness),
            new(0, bottom - thickness, armX, thickness), new(0, bottom - armY, thickness, armY),
            new(0, middleY - armY / 2f, thickness, armY)
        ];
    }

    /// <summary>
    /// Le contour pointillé d'un rectangle : des tirets de <paramref name="dash"/> séparés
    /// d'autant, tracés vers l'intérieur comme le cadre plein. Sert à montrer la source
    /// entière, partie rognée comprise, pendant qu'on la rogne.
    /// </summary>
    internal static IReadOnlyList<PreviewFillRect> DashedEdges(
        double x,
        double y,
        double width,
        double height,
        float thickness,
        float dash)
    {
        if (width <= 0 || height <= 0 || thickness <= 0 || dash <= 0) return [];

        var rects = new List<PreviewFillRect>();
        var left = (float)x;
        var top = (float)y;
        var boxWidth = (float)width;
        var boxHeight = (float)height;

        for (var offset = 0f; offset < boxWidth; offset += dash * 2)
        {
            var length = Math.Min(dash, boxWidth - offset);
            rects.Add(new PreviewFillRect(left + offset, top, length, thickness));
            rects.Add(new PreviewFillRect(left + offset, top + boxHeight - thickness, length, thickness));
        }

        for (var offset = 0f; offset < boxHeight; offset += dash * 2)
        {
            var length = Math.Min(dash, boxHeight - offset);
            rects.Add(new PreviewFillRect(left, top + offset, thickness, length));
            rects.Add(new PreviewFillRect(left + boxWidth - thickness, top + offset, thickness, length));
        }

        return rects;
    }

    /// <summary>Ce que la pastille d'une source zoomée écrit, au format du curseur de zoom.</summary>
    internal static string ZoomLabel(SourceZoom zoom) =>
        string.Create(CultureInfo.CurrentCulture, $"Zoom ×{zoom.Factor:0.0}");

    /// <summary>
    /// Où se pose la pastille de zoom d'une source, dans le repère de son cadre : dans le coin
    /// haut-gauche, à la taille de l'écran (<paramref name="imageWidth"/> ×
    /// <paramref name="imageHeight"/> pixels physiques). Rien sans zoom, ou si le cadre est
    /// trop petit pour la porter.
    /// </summary>
    internal static PreviewFillRect? ZoomBadge(
        SourceTransform transform,
        int imageWidth,
        int imageHeight,
        OverlayMetrics metrics)
    {
        if (!transform.Zoom.IsZoomed || imageWidth <= 0 || imageHeight <= 0) return null;

        var badge = new PreviewFillRect(
            metrics.BadgeInset,
            metrics.BadgeInset,
            imageWidth * metrics.CanvasPerScreenPixel,
            imageHeight * metrics.CanvasPerScreenPixel);
        return badge.X + badge.Width > transform.Width - metrics.BadgeInset ||
               badge.Y + badge.Height > transform.Height - metrics.BadgeInset
            ? null
            : badge;
    }

    /// <summary>
    /// La mini-carte d'une source zoomée, dans le repère de son cadre : sa plaque, le contour
    /// de l'image entière, et la fenêtre que le cadre en montre. Rien sans zoom, ou si le
    /// cadre est trop petit pour qu'elle se lise.
    /// </summary>
    internal static (PreviewFillRect Plate, IReadOnlyList<PreviewFillRect> Outline, PreviewFillRect Window,
        IReadOnlyList<PreviewFillRect> WindowEdges)? ZoomMapShapes(SourceTransform transform, OverlayMetrics metrics)
    {
        if (!transform.Zoom.IsZoomed) return null;
        if (ZoomMap.Bounds(transform.Width, transform.Height, metrics.CanvasPerScreenPixel) is not { } map) return null;

        var window = ZoomMap.Window(transform.Zoom);
        var windowRect = new PreviewFillRect(
            (float)(map.X + window.X * map.Width),
            (float)(map.Y + window.Y * map.Height),
            (float)(window.Width * map.Width),
            (float)(window.Height * map.Height));

        return (
            new PreviewFillRect((float)map.X, (float)map.Y, (float)map.Width, (float)map.Height),
            BoxEdges(map.X, map.Y, map.Width, map.Height, metrics.IdleThickness),
            windowRect,
            BoxEdges(windowRect.X, windowRect.Y, windowRect.Width, windowRect.Height, metrics.ChosenThickness));
    }

    /// <summary>
    /// Peint la scène puis, par-dessus, l'overlay de composition. Tout passe par la même
    /// projection : l'overlay tombe exactement sur l'image, au pixel près et à la même image
    /// que le moteur.
    /// </summary>
    internal static void RenderScene(
        ObsDisplayFrame frame,
        ObsSource sceneSource,
        uint canvasWidth,
        uint canvasHeight,
        CompositionOverlay overlay)
    {
        var viewport = CalculateViewport(frame.Width, frame.Height, canvasWidth, canvasHeight);
        if (viewport.Width == 0 || viewport.Height == 0) return;

        GsViewportPush();
        try
        {
            GsProjectionPush();
            try
            {
                GsOrtho(0, canvasWidth, 0, canvasHeight, -100, 100);
                GsSetViewport(viewport.X, viewport.Y, viewport.Width, viewport.Height);
                frame.Render(sceneSource);
                var metrics = MetricsFor(viewport.Width, canvasWidth);
                DrawOverlay(overlay, metrics, canvasWidth, canvasHeight);
                // Les pastilles passent au-dessus de tout le reste de l'overlay.
                if (!_outlinesUnavailable)
                {
                    try
                    {
                        DrawZoomBadges(overlay, metrics);
                    }
                    catch (Exception)
                    {
                        _outlinesUnavailable = true;
                    }
                }
            }
            finally
            {
                GsProjectionPop();
            }
        }
        finally
        {
            GsViewportPop();
        }
    }

    private static void DrawOverlay(
        CompositionOverlay overlay,
        OverlayMetrics metrics,
        uint canvasWidth,
        uint canvasHeight)
    {
        if (_outlinesUnavailable || overlay.Sources.Count == 0) return;

        try
        {
            var effect = ObsGetBaseEffect(ObsEffectSolid);
            if (effect == IntPtr.Zero) return;

            var colorParameter = GsEffectGetParamByName(effect, "color");
            if (colorParameter == IntPtr.Zero) return;

            var chosen = overlay.Selected;
            var cropping = overlay.IsCropping && chosen != null;
            var guides = GuideRects(overlay.Guides ?? [], canvasWidth, canvasHeight, metrics.IdleThickness);

            // L'ombre passe sous tout l'overlay, d'un pixel de chaque côté. Un cœur coloré
            // posé sur un liseré sombre se lit sur n'importe quelle image ; le même trait
            // seul disparaît dès que l'image prend sa valeur.
            SetColor(colorParameter, Shadow);
            while (GsEffectLoop(effect, "Solid"))
            {
                foreach (var source in overlay.Sources)
                    FillAllGrown(source.Transform, Edges(source, Thickness(source, overlay, metrics)), metrics.Shadow);

                FillAllGrown(CanvasFrame, guides, metrics.Shadow);

                if (chosen == null) continue;

                if (cropping)
                {
                    FillAllGrown(chosen.Transform, Uncropped(chosen, metrics), metrics.Shadow);
                    FillAllGrown(chosen.Transform, Brackets(chosen, metrics), metrics.Shadow);
                }
                else
                {
                    FillAllGrown(chosen.Transform, Handles(chosen, metrics.Handle), metrics.Shadow);
                }
            }

            // Un cadre par couleur : celle de la source, celle que porte déjà sa pastille
            // dans la liste. C'est l'identité, elle ne dit pas l'état. Le contour pointillé de
            // la source entière la garde aussi : c'est toujours la même source.
            foreach (var source in overlay.Sources)
            {
                SetColor(colorParameter, ToVec4(source.Tint));
                while (GsEffectLoop(effect, "Solid"))
                {
                    FillAll(source.Transform, Edges(source, Thickness(source, overlay, metrics)));
                    if (cropping && source == chosen) FillAll(source.Transform, Uncropped(source, metrics));
                }
            }

            // Les guides ne sont à aucune source : ils prennent la couleur neutre de l'outil,
            // jamais celle d'une pastille qu'on pourrait confondre avec un cadre.
            if (guides.Count > 0)
            {
                SetColor(colorParameter, HandleCore);
                while (GsEffectLoop(effect, "Solid")) FillAll(CanvasFrame, guides);
            }

            DrawZoomMap(effect, colorParameter, overlay, metrics);

            if (chosen == null) return;

            // L'état, lui, tient au poids du trait et à ces poignées, qui prennent l'accent
            // de la sélection comme partout ailleurs dans l'interface.
            SetColor(colorParameter, Chosen);
            while (GsEffectLoop(effect, "Solid"))
            {
                FillAll(chosen.Transform, cropping
                    ? Brackets(chosen, metrics)
                    : Handles(chosen, metrics.Handle));
            }

            if (cropping) return;

            SetColor(colorParameter, HandleCore);
            while (GsEffectLoop(effect, "Solid")) FillAll(chosen.Transform, Handles(chosen, metrics.HandleCore));
        }
        catch (Exception)
        {
            _outlinesUnavailable = true;
        }
    }

    // La mini-carte de la source choisie et zoomée. Sa plaque est sombre et un peu
    // transparente : elle se lit sur n'importe quelle image sans la cacher. Le contour de
    // l'image prend la couleur neutre de l'outil ; la fenêtre, l'accent de la sélection,
    // parce que c'est elle qu'on saisit.
    private static void DrawZoomMap(
        IntPtr effect,
        IntPtr colorParameter,
        CompositionOverlay overlay,
        OverlayMetrics metrics)
    {
        if (overlay.Selected?.Transform is not { } chosen) return;
        if (ZoomMapShapes(chosen, metrics) is not { } map) return;

        SetColor(colorParameter, Plate);
        while (GsEffectLoop(effect, "Solid")) Fill(chosen, map.Plate);

        SetColor(colorParameter, HandleCore);
        while (GsEffectLoop(effect, "Solid")) FillAll(chosen, map.Outline);

        SetColor(colorParameter, ChosenVeil);
        while (GsEffectLoop(effect, "Solid")) Fill(chosen, map.Window);

        SetColor(colorParameter, Chosen);
        while (GsEffectLoop(effect, "Solid")) FillAll(chosen, map.WindowEdges);
    }

    // La pastille de zoom de chaque source zoomée, rendue par l'interface dans le style de
    // celle qui nomme la scène. Une pastille pas encore rendue est demandée, et paraît à
    // l'image suivante.
    private static void DrawZoomBadges(CompositionOverlay overlay, OverlayMetrics metrics)
    {
        IntPtr effect = IntPtr.Zero;
        IntPtr imageParameter = IntPtr.Zero;
        foreach (var source in overlay.Sources)
        {
            var transform = source.Transform;
            if (!transform.Zoom.IsZoomed) continue;
            var label = ZoomLabel(transform.Zoom);
            if (PreviewBadges.Get(label) is not { } image) continue;
            if (ZoomBadge(transform, image.Width, image.Height, metrics) is not { } badge) continue;

            var texture = TextureOf(label, image);
            if (texture == IntPtr.Zero) continue;

            if (effect == IntPtr.Zero)
            {
                effect = ObsGetBaseEffect(ObsEffectDefault);
                if (effect == IntPtr.Zero) return;
                imageParameter = GsEffectGetParamByName(effect, "image");
                if (imageParameter == IntPtr.Zero) return;
            }

            GsEffectSetTexture(imageParameter, texture);
            while (GsEffectLoop(effect, "Draw")) Fill(transform, badge, texture, image.Width, image.Height);
        }
    }

    // Une texture par texte de pastille, créée au premier besoin et gardée : il n'y en a
    // qu'une par facteur de zoom affiché, chacune de quelques kilo-octets. Une pastille
    // rendue à nouveau (autre échelle d'écran) remplace la sienne, qui est détruite.
    // N'est touché que dans le contexte graphique de libobs : il sérialise les accès.
    private static readonly Dictionary<string, (PreviewBadgeImage Image, IntPtr Texture)> Textures = new();

    private static IntPtr TextureOf(string label, PreviewBadgeImage image)
    {
        if (Textures.TryGetValue(label, out var cached))
        {
            if (ReferenceEquals(cached.Image, image)) return cached.Texture;
            if (cached.Texture != IntPtr.Zero) GsTextureDestroy(cached.Texture);
        }

        var handle = GCHandle.Alloc(image.Pixels, GCHandleType.Pinned);
        IntPtr texture;
        try
        {
            var levels = new[] { handle.AddrOfPinnedObject() };
            texture = GsTextureCreate((uint)image.Width, (uint)image.Height, GsColorFormatBgra, 1, levels, 0);
        }
        finally
        {
            handle.Free();
        }

        Textures[label] = (image, texture);
        return texture;
    }

    /// <summary>
    /// Détruit les textures des pastilles. À appeler avant d'arrêter libobs, une fois les
    /// aperçus fermés : passé l'arrêt, plus rien ne peut les rendre.
    /// </summary>
    internal static void ReleaseTextures()
    {
        // Le contexte graphique d'abord, comme le thread de rendu qui le tient déjà quand il
        // touche à ces textures : le même ordre partout, pas d'interblocage possible.
        ObsEnterGraphics();
        try
        {
            foreach (var (_, texture) in Textures.Values)
            {
                if (texture != IntPtr.Zero) GsTextureDestroy(texture);
            }

            Textures.Clear();
        }
        finally
        {
            ObsLeaveGraphics();
        }
    }

    /// <summary>
    /// Les guides d'alignement, chacun d'un bord à l'autre du canvas et centré sur sa ligne :
    /// c'est la ligne elle-même que la source vient de toucher.
    /// </summary>
    internal static IReadOnlyList<PreviewFillRect> GuideRects(
        IReadOnlyList<CompositionGuide> guides,
        uint canvasWidth,
        uint canvasHeight,
        float thickness)
    {
        if (guides.Count == 0 || canvasWidth == 0 || canvasHeight == 0 || thickness <= 0) return [];

        var rects = new List<PreviewFillRect>(guides.Count);
        foreach (var guide in guides)
        {
            var position = (float)guide.Position - thickness / 2f;
            rects.Add(guide.IsVertical
                ? new PreviewFillRect(position, 0, thickness, canvasHeight)
                : new PreviewFillRect(0, position, canvasWidth, thickness));
        }

        return rects;
    }

    // Le choix et le survol se disent par le même poids : le survol annonce ce que le clic
    // va prendre, avec le trait qu'il aura une fois pris.
    private static float Thickness(OverlaySource source, CompositionOverlay overlay, OverlayMetrics metrics) =>
        IsSame(source, overlay.Selected) || IsSame(source, overlay.Hovered)
            ? metrics.ChosenThickness
            : metrics.IdleThickness;

    private static bool IsSame(OverlaySource source, OverlaySource? other) =>
        other != null && source.Transform.SourceId == other.Transform.SourceId;

    // Le repère du canvas lui-même, pour ce qui n'appartient à aucune source.
    private static readonly SourceTransform CanvasFrame =
        new(Guid.Empty, 0, 0, 0, 0, 1, 1, SourceCrop.None, 0, 0, true);

    // Toutes les formes se tracent dans le repère de leur source : origine en son point
    // (X, Y), axes tournés avec elle. Fill les pose ensuite dans le canvas.
    private static IReadOnlyList<PreviewFillRect> Edges(OverlaySource source, float thickness) =>
        BoxEdges(0, 0, source.Transform.Width, source.Transform.Height, thickness);

    private static IReadOnlyList<PreviewFillRect> Handles(OverlaySource source, float size) =>
        HandleRects(0, 0, source.Transform.Width, source.Transform.Height, size);

    private static IReadOnlyList<PreviewFillRect> Brackets(OverlaySource source, OverlayMetrics metrics) =>
        CropBrackets(source.Transform.Width, source.Transform.Height, metrics.CropArm, metrics.CropThickness);

    // La source entière, rognage compris : elle déborde du cadre du côté de ce qui est rogné.
    private static IReadOnlyList<PreviewFillRect> Uncropped(OverlaySource source, OverlayMetrics metrics)
    {
        var transform = source.Transform;
        return DashedEdges(
            -transform.Crop.Left * transform.ScaleX,
            -transform.Crop.Top * transform.ScaleY,
            transform.SourceWidth * transform.ScaleX,
            transform.SourceHeight * transform.ScaleY,
            metrics.ChosenThickness,
            metrics.Dash);
    }

    private static Vec4 ToVec4(OverlayTint tint) => new(tint.Red, tint.Green, tint.Blue, 1f);

    private static void SetColor(IntPtr parameter, Vec4 color) => GsEffectSetVec4(parameter, ref color);

    private static void FillAll(SourceTransform frame, IReadOnlyList<PreviewFillRect> rects)
    {
        foreach (var rect in rects) Fill(frame, rect);
    }

    private static void FillAllGrown(SourceTransform frame, IReadOnlyList<PreviewFillRect> rects, float amount)
    {
        foreach (var rect in rects)
        {
            Fill(frame, new PreviewFillRect(
                rect.X - amount,
                rect.Y - amount,
                rect.Width + amount * 2,
                rect.Height + amount * 2));
        }
    }

    // Un quad unitaire mis à la place et à la taille voulues : c'est ainsi que libobs peint
    // ses propres rectangles, sans avoir à construire de géométrie. Le repère de la source
    // vient d'abord — son point, puis sa rotation —, exactement comme libobs compose l'item.
    private static void Fill(SourceTransform frame, PreviewFillRect rect) =>
        Fill(frame, rect, IntPtr.Zero, 1, 1);

    // Une texture se peint de même : gs_draw_sprite la pose sur un quad de sa propre taille,
    // ramené ensuite à celle du rectangle.
    private static void Fill(SourceTransform frame, PreviewFillRect rect, IntPtr texture, int width, int height)
    {
        GsMatrixPush();
        try
        {
            GsMatrixIdentity();
            GsMatrixTranslate3f((float)frame.X, (float)frame.Y, 0f);
            if (frame.Rotation != 0)
                GsMatrixRotaa4f(0f, 0f, 1f, (float)(frame.Rotation * Math.PI / 180));
            GsMatrixTranslate3f(rect.X, rect.Y, 0f);
            GsMatrixScale3f(rect.Width / width, rect.Height / height, 1f);
            GsDrawSprite(texture, 0, (uint)width, (uint)height);
        }
        finally
        {
            GsMatrixPop();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Vec4(float x, float y, float z, float w)
    {
        public readonly float X = x;
        public readonly float Y = y;
        public readonly float Z = z;
        public readonly float W = w;
    }

    [DllImport(ObsLibrary, EntryPoint = "gs_viewport_push", CallingConvention = CallingConvention.Cdecl)]
    private static extern void GsViewportPush();

    [DllImport(ObsLibrary, EntryPoint = "gs_viewport_pop", CallingConvention = CallingConvention.Cdecl)]
    private static extern void GsViewportPop();

    [DllImport(ObsLibrary, EntryPoint = "gs_projection_push", CallingConvention = CallingConvention.Cdecl)]
    private static extern void GsProjectionPush();

    [DllImport(ObsLibrary, EntryPoint = "gs_projection_pop", CallingConvention = CallingConvention.Cdecl)]
    private static extern void GsProjectionPop();

    [DllImport(ObsLibrary, EntryPoint = "gs_set_viewport", CallingConvention = CallingConvention.Cdecl)]
    private static extern void GsSetViewport(int x, int y, int width, int height);

    [DllImport(ObsLibrary, EntryPoint = "gs_ortho", CallingConvention = CallingConvention.Cdecl)]
    private static extern void GsOrtho(
        float left,
        float right,
        float top,
        float bottom,
        float near,
        float far);

    [DllImport(ObsLibrary, EntryPoint = "obs_get_base_effect", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr ObsGetBaseEffect(int effect);

    [DllImport(ObsLibrary, EntryPoint = "gs_effect_get_param_by_name", CallingConvention = CallingConvention.Cdecl,
        CharSet = CharSet.Ansi)]
    private static extern IntPtr GsEffectGetParamByName(IntPtr effect, string name);

    [DllImport(ObsLibrary, EntryPoint = "gs_effect_set_vec4", CallingConvention = CallingConvention.Cdecl)]
    private static extern void GsEffectSetVec4(IntPtr parameter, ref Vec4 value);

    [DllImport(ObsLibrary, EntryPoint = "gs_effect_loop", CallingConvention = CallingConvention.Cdecl,
        CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool GsEffectLoop(IntPtr effect, string technique);

    [DllImport(ObsLibrary, EntryPoint = "gs_matrix_push", CallingConvention = CallingConvention.Cdecl)]
    private static extern void GsMatrixPush();

    [DllImport(ObsLibrary, EntryPoint = "gs_matrix_pop", CallingConvention = CallingConvention.Cdecl)]
    private static extern void GsMatrixPop();

    [DllImport(ObsLibrary, EntryPoint = "gs_matrix_identity", CallingConvention = CallingConvention.Cdecl)]
    private static extern void GsMatrixIdentity();

    [DllImport(ObsLibrary, EntryPoint = "gs_matrix_translate3f", CallingConvention = CallingConvention.Cdecl)]
    private static extern void GsMatrixTranslate3f(float x, float y, float z);

    [DllImport(ObsLibrary, EntryPoint = "gs_matrix_rotaa4f", CallingConvention = CallingConvention.Cdecl)]
    private static extern void GsMatrixRotaa4f(float x, float y, float z, float angle);

    [DllImport(ObsLibrary, EntryPoint = "gs_matrix_scale3f", CallingConvention = CallingConvention.Cdecl)]
    private static extern void GsMatrixScale3f(float x, float y, float z);

    [DllImport(ObsLibrary, EntryPoint = "gs_texture_create", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr GsTextureCreate(
        uint width,
        uint height,
        int colorFormat,
        uint levels,
        IntPtr[] data,
        uint flags);

    [DllImport(ObsLibrary, EntryPoint = "gs_texture_destroy", CallingConvention = CallingConvention.Cdecl)]
    private static extern void GsTextureDestroy(IntPtr texture);

    [DllImport(ObsLibrary, EntryPoint = "obs_enter_graphics", CallingConvention = CallingConvention.Cdecl)]
    private static extern void ObsEnterGraphics();

    [DllImport(ObsLibrary, EntryPoint = "obs_leave_graphics", CallingConvention = CallingConvention.Cdecl)]
    private static extern void ObsLeaveGraphics();

    [DllImport(ObsLibrary, EntryPoint = "gs_effect_set_texture", CallingConvention = CallingConvention.Cdecl)]
    private static extern void GsEffectSetTexture(IntPtr parameter, IntPtr texture);

    [DllImport(ObsLibrary, EntryPoint = "gs_draw_sprite", CallingConvention = CallingConvention.Cdecl)]
    private static extern void GsDrawSprite(IntPtr texture, uint flip, uint width, uint height);
}
