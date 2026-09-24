using System.Runtime.InteropServices;
using CastorApplication.Models.Studio;
using LibObs;

namespace CastorApplication.Services.Studio;

internal readonly record struct PreviewViewport(int X, int Y, int Width, int Height);

/// <summary>Un rectangle plein à peindre, dans les coordonnées du canvas du moteur.</summary>
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
    float Shadow);

internal static class ObsPreviewGraphics
{
    private const string ObsLibrary = "obs";

    // obs_base_effect : l'effet « solid » est le quatrième de l'énumération de libobs.
    private const int ObsEffectSolid = 3;

    // Le cadre d'une source porte la couleur de sa pastille dans la liste : sur une
    // composition qui se chevauche, c'est ce qui dit quel cadre est quelle ligne. Ce qui
    // reste vient de Styles/Colors.axaml, dans les valeurs du thème sombre : la zone
    // d'aperçu est noire en clair comme en sombre.
    private static readonly Vec4 Chosen = new(0.357f, 0.553f, 0.937f, 1f);     // AppAccentFg
    private static readonly Vec4 HandleCore = new(0.918f, 0.918f, 0.941f, 1f); // AppFg1
    private static readonly Vec4 Shadow = new(0.043f, 0.043f, 0.071f, 1f);     // AppBg

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
        Shadow: ToCanvasPixels(1f, viewportWidth, canvasWidth, minimum: 1f));

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
                DrawOverlay(overlay, MetricsFor(viewport.Width, canvasWidth));
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

    private static void DrawOverlay(CompositionOverlay overlay, OverlayMetrics metrics)
    {
        if (_outlinesUnavailable || overlay.Sources.Count == 0) return;

        try
        {
            var effect = ObsGetBaseEffect(ObsEffectSolid);
            if (effect == IntPtr.Zero) return;

            var colorParameter = GsEffectGetParamByName(effect, "color");
            if (colorParameter == IntPtr.Zero) return;

            var chosen = overlay.Selected;

            // L'ombre passe sous tout l'overlay, d'un pixel de chaque côté. Un cœur coloré
            // posé sur un liseré sombre se lit sur n'importe quelle image ; le même trait
            // seul disparaît dès que l'image prend sa valeur.
            SetColor(colorParameter, Shadow);
            while (GsEffectLoop(effect, "Solid"))
            {
                foreach (var source in overlay.Sources)
                    FillAllGrown(Edges(source, Thickness(source, chosen, metrics)), metrics.Shadow);

                if (chosen != null) FillAllGrown(Handles(chosen, metrics.Handle), metrics.Shadow);
            }

            // Un cadre par couleur : celle de la source, celle que porte déjà sa pastille
            // dans la liste. C'est l'identité, elle ne dit pas l'état.
            foreach (var source in overlay.Sources)
            {
                SetColor(colorParameter, ToVec4(source.Tint));
                while (GsEffectLoop(effect, "Solid"))
                    FillAll(Edges(source, Thickness(source, chosen, metrics)));
            }

            if (chosen == null) return;

            // L'état, lui, tient au poids du trait et à ces poignées, qui prennent l'accent
            // de la sélection comme partout ailleurs dans l'interface.
            SetColor(colorParameter, Chosen);
            while (GsEffectLoop(effect, "Solid")) FillAll(Handles(chosen, metrics.Handle));

            SetColor(colorParameter, HandleCore);
            while (GsEffectLoop(effect, "Solid")) FillAll(Handles(chosen, metrics.HandleCore));
        }
        catch (Exception)
        {
            _outlinesUnavailable = true;
        }
    }

    private static float Thickness(OverlaySource source, OverlaySource? chosen, OverlayMetrics metrics) =>
        chosen != null && source.Transform.SourceId == chosen.Transform.SourceId
            ? metrics.ChosenThickness
            : metrics.IdleThickness;

    private static IReadOnlyList<PreviewFillRect> Edges(OverlaySource source, float thickness) =>
        BoxEdges(
            source.Transform.X, source.Transform.Y,
            source.Transform.Width, source.Transform.Height,
            thickness);

    private static IReadOnlyList<PreviewFillRect> Handles(OverlaySource source, float size) =>
        HandleRects(
            source.Transform.X, source.Transform.Y,
            source.Transform.Width, source.Transform.Height,
            size);

    private static Vec4 ToVec4(OverlayTint tint) => new(tint.Red, tint.Green, tint.Blue, 1f);

    private static void SetColor(IntPtr parameter, Vec4 color) => GsEffectSetVec4(parameter, ref color);

    private static void FillAll(IReadOnlyList<PreviewFillRect> rects)
    {
        foreach (var rect in rects) Fill(rect);
    }

    private static void FillAllGrown(IReadOnlyList<PreviewFillRect> rects, float amount)
    {
        foreach (var rect in rects)
        {
            Fill(new PreviewFillRect(
                rect.X - amount,
                rect.Y - amount,
                rect.Width + amount * 2,
                rect.Height + amount * 2));
        }
    }

    // Un quad unitaire mis à la place et à la taille voulues : c'est ainsi que libobs peint
    // ses propres rectangles, sans avoir à construire de géométrie.
    private static void Fill(PreviewFillRect rect)
    {
        GsMatrixPush();
        try
        {
            GsMatrixIdentity();
            GsMatrixTranslate3f(rect.X, rect.Y, 0f);
            GsMatrixScale3f(rect.Width, rect.Height, 1f);
            GsDrawSprite(IntPtr.Zero, 0, 1, 1);
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

    [DllImport(ObsLibrary, EntryPoint = "gs_matrix_scale3f", CallingConvention = CallingConvention.Cdecl)]
    private static extern void GsMatrixScale3f(float x, float y, float z);

    [DllImport(ObsLibrary, EntryPoint = "gs_draw_sprite", CallingConvention = CallingConvention.Cdecl)]
    private static extern void GsDrawSprite(IntPtr texture, uint flip, uint width, uint height);
}
