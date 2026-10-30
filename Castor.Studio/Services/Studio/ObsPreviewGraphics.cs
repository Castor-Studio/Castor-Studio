using System.Globalization;
using System.Runtime.InteropServices;
using CastorApplication.Models.Studio;
using LibObs;

namespace CastorApplication.Services.Studio;

internal readonly record struct PreviewViewport(int X, int Y, int Width, int Height);

/// <summary>
/// A filled rectangle to paint, in a source's coordinates: origin at its point (X, Y),
/// axes turned with it.
/// </summary>
internal readonly record struct PreviewFillRect(float X, float Y, float Width, float Height);

/// <summary>
/// Overlay sizes, in canvas pixels, for the current frame. They are thought of
/// in screen pixels then converted: a handle is aimed with the mouse, it does not follow
/// the scale at which the canvas is reduced in the panel.
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

    // obs_base_effect: the "solid" effect is the fourth in libobs's enumeration.
    private const int ObsEffectSolid = 3;

    // obs_base_effect: the default effect, which paints a texture, is the first.
    private const int ObsEffectDefault = 0;

    // gs_color_format: GS_BGRA, the pixel order Avalonia renders.
    private const int GsColorFormatBgra = 5;

    // A source's outline takes the color of its dot in the list: on an
    // overlapping composition, this tells which outline is which row. The
    // rest comes from Styles/Colors.axaml, in the dark theme's values: the preview
    // area is black in light and dark themes alike.
    private static readonly Vec4 Chosen = new(0.357f, 0.553f, 0.937f, 1f);     // AppAccentFg
    private static readonly Vec4 HandleCore = new(0.918f, 0.918f, 0.941f, 1f); // AppFg1
    private static readonly Vec4 Shadow = new(0.043f, 0.043f, 0.071f, 1f);     // AppBg
    private static readonly Vec4 Plate = new(0.043f, 0.043f, 0.071f, 0.78f);   // AppBg, veiled
    private static readonly Vec4 ChosenVeil = new(0.357f, 0.553f, 0.937f, 0.3f); // AppAccentFg, veiled

    // A missing graphics symbol must not take down libobs's render thread: on the
    // first failure the overlay is given up for good, the picture carries on.
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
    /// The four edges of an outline, drawn <em>inside</em> the source's rectangle:
    /// an outline straddling the edge would make the source look larger than it
    /// is, when its actual size is exactly what it shows.
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

        // A source thinner than two strokes is painted solid: two overlapping
        // edges would draw an outline thicker than the source itself.
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
    /// A source's eight handles: four at the corners, four at the middle of the sides.
    /// Each is centered on its point, so it straddles the edge — that makes it
    /// grabbable from both sides of the stroke, and leaves it visible on a source
    /// flush with the canvas edge.
    /// </summary>
    /// <remarks>
    /// Returned clockwise from the top-left corner: this is
    /// the order the stretch gesture uses to know which edge it pulls.
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
    /// The crop handles: a bracket at each corner, a bar at the middle of each
    /// side, set <em>inside</em> the edge. A different shape from the stretch
    /// squares, because the same gesture does something else here: instead of pulling the
    /// source, it removes edges from it.
    /// </summary>
    /// <remarks>
    /// Eight handles in the same order as <see cref="HandleRects"/> — each one or two
    /// rectangles —, on a rectangle with origin (0, 0).
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
        // A source smaller than two brackets keeps arms that do not cross.
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
    /// A rectangle's dotted outline: dashes of <paramref name="dash"/> separated
    /// by as much, drawn inward like the solid outline. Used to show the whole
    /// source, cropped part included, during cropping.
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

    /// <summary>What a zoomed source's badge says, in the zoom slider's format.</summary>
    internal static string ZoomLabel(SourceZoom zoom) =>
        string.Create(CultureInfo.CurrentCulture, $"Zoom ×{zoom.Factor:0.0}");

    /// <summary>
    /// Where a source's zoom badge sits, in its frame's coordinates: in the top-left
    /// corner, at screen size (<paramref name="imageWidth"/> ×
    /// <paramref name="imageHeight"/> physical pixels). Nothing without zoom, or if the frame is
    /// too small to carry it.
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
    /// A zoomed source's mini-map, in its frame's coordinates: its plate, the outline
    /// of the whole picture, and the window the frame shows. Nothing without zoom, or if the
    /// frame is too small for it to be read.
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
    /// Paints the scene then, on top, the composition overlay. Everything goes through the same
    /// projection: the overlay lands exactly on the picture, to the pixel and on the same frame
    /// as the engine.
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
                // Badges go above everything else in the overlay.
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

            // The shadow goes under the whole overlay, one pixel on each side. A colored core
            // on a dark outline reads on any picture; the same stroke
            // alone vanishes as soon as the picture takes its value.
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

            // One outline per color: the source's, the one its dot already has
            // in the list. That is identity, it does not state the state. The dotted outline of
            // the whole source keeps it too: it is still the same source.
            foreach (var source in overlay.Sources)
            {
                SetColor(colorParameter, ToVec4(source.Tint));
                while (GsEffectLoop(effect, "Solid"))
                {
                    FillAll(source.Transform, Edges(source, Thickness(source, overlay, metrics)));
                    if (cropping && source == chosen) FillAll(source.Transform, Uncropped(source, metrics));
                }
            }

            // Guides belong to no source: they take the tool's neutral color,
            // never that of a dot that could be mistaken for an outline.
            if (guides.Count > 0)
            {
                SetColor(colorParameter, HandleCore);
                while (GsEffectLoop(effect, "Solid")) FillAll(CanvasFrame, guides);
            }

            DrawZoomMap(effect, colorParameter, overlay, metrics);

            if (chosen == null) return;

            // State comes from stroke weight and these handles, which take the
            // selection accent as everywhere else in the interface.
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

    // The selected and zoomed source's mini-map. Its plate is dark and slightly
    // transparent: it reads on any picture without hiding it. The picture
    // outline takes the tool's neutral color; the window, the selection accent,
    // because it is what gets grabbed.
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

    // The zoom badge of each zoomed source, rendered by the interface in the style of
    // the one naming the scene. A badge not yet rendered is requested, and appears on
    // the next frame.
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

    // One texture per badge text, created on first need and kept: there is
    // only one per displayed zoom factor, each a few kilobytes. A badge
    // rendered again (other screen scale) replaces its own, which is destroyed.
    // Only touched inside libobs's graphics context: that serializes access.
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
    /// Destroys the badge textures. Call before stopping libobs, once
    /// previews are closed: after shutdown, nothing can render them.
    /// </summary>
    internal static void ReleaseTextures()
    {
        // The graphics context first, like the render thread that already holds it when it
        // touches these textures: same order everywhere, no deadlock possible.
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
    /// Alignment guides, each from one edge of the canvas to the other and centered on its line:
    /// it is the line itself the source has just touched.
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

    // Selection and hover are shown by the same weight: hover announces what the click
    // will take, with the stroke it will have once taken.
    private static float Thickness(OverlaySource source, CompositionOverlay overlay, OverlayMetrics metrics) =>
        IsSame(source, overlay.Selected) || IsSame(source, overlay.Hovered)
            ? metrics.ChosenThickness
            : metrics.IdleThickness;

    private static bool IsSame(OverlaySource source, OverlaySource? other) =>
        other != null && source.Transform.SourceId == other.Transform.SourceId;

    // The canvas's own coordinates, for what belongs to no source.
    private static readonly SourceTransform CanvasFrame =
        new(Guid.Empty, 0, 0, 0, 0, 1, 1, SourceCrop.None, 0, 0, true);

    // All shapes are drawn in their source's coordinates: origin at its point
    // (X, Y), axes turned with it. Fill then places them in the canvas.
    private static IReadOnlyList<PreviewFillRect> Edges(OverlaySource source, float thickness) =>
        BoxEdges(0, 0, source.Transform.Width, source.Transform.Height, thickness);

    private static IReadOnlyList<PreviewFillRect> Handles(OverlaySource source, float size) =>
        HandleRects(0, 0, source.Transform.Width, source.Transform.Height, size);

    private static IReadOnlyList<PreviewFillRect> Brackets(OverlaySource source, OverlayMetrics metrics) =>
        CropBrackets(source.Transform.Width, source.Transform.Height, metrics.CropArm, metrics.CropThickness);

    // The whole source, crop included: it overflows the frame on the cropped side.
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

    // A unit quad moved and sized as needed: this is how libobs paints
    // its own rectangles, without building geometry. The source's coordinates
    // come first — its point, then its rotation —, exactly as libobs composes the item.
    private static void Fill(SourceTransform frame, PreviewFillRect rect) =>
        Fill(frame, rect, IntPtr.Zero, 1, 1);

    // A texture paints the same way: gs_draw_sprite lays it on a quad of its own size,
    // then brought to the rectangle's size.
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
