using System.Runtime.InteropServices;
using CastorApplication.Models.Studio;
using LibObs;

namespace CastorApplication.Services.Studio;

internal readonly record struct PreviewViewport(int X, int Y, int Width, int Height);

/// <summary>Un rectangle plein à peindre, dans les coordonnées du canvas du moteur.</summary>
internal readonly record struct PreviewFillRect(float X, float Y, float Width, float Height);

/// <summary>
/// Les tailles de l'overlay, en pixels du canvas, pour l'image en cours. Elles sont pensées
/// en pixels de l'écran puis converties : une marque se vise à la souris, elle ne suit pas
/// l'échelle à laquelle le canvas est réduit dans le panneau.
/// </summary>
internal readonly record struct OverlayMetrics(
    float Dash,
    float DashThickness,
    float IdleThickness,
    float IdleArm,
    float MarkThickness,
    float MarkArm,
    float MarkBar,
    float Backing);

internal static class ObsPreviewGraphics
{
    private const string ObsLibrary = "obs";

    // obs_base_effect : l'effet « solid » est le quatrième de l'énumération de libobs.
    private const int ObsEffectSolid = 3;

    // L'overlay est achromatique. Dans cette application la couleur porte un état — le rouge
    // du direct et de l'enregistrement, le bleu de la sélection dans les listes. La
    // géométrie n'emprunte pas ce vocabulaire : elle ne dit pas un état, elle dit une forme.
    private static readonly Vec4 Ink = new(0.910f, 0.918f, 0.949f, 1f);
    private static readonly Vec4 InkShadow = new(0.043f, 0.043f, 0.071f, 1f);

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
        Dash: ToCanvasPixels(9f, viewportWidth, canvasWidth, minimum: 3f),
        DashThickness: ToCanvasPixels(2f, viewportWidth, canvasWidth, minimum: 1f),
        IdleThickness: ToCanvasPixels(1.5f, viewportWidth, canvasWidth, minimum: 1f),
        IdleArm: ToCanvasPixels(10f, viewportWidth, canvasWidth, minimum: 3f),
        MarkThickness: ToCanvasPixels(3f, viewportWidth, canvasWidth, minimum: 1f),
        MarkArm: ToCanvasPixels(14f, viewportWidth, canvasWidth, minimum: 4f),
        MarkBar: ToCanvasPixels(20f, viewportWidth, canvasWidth, minimum: 6f),
        Backing: ToCanvasPixels(1.5f, viewportWidth, canvasWidth, minimum: 1f));

    private static float ToCanvasPixels(float devicePixels, int viewportWidth, uint canvasWidth, float minimum)
    {
        if (viewportWidth <= 0 || canvasWidth == 0) return devicePixels;

        return Math.Max(minimum, devicePixels * canvasWidth / viewportWidth);
    }

    /// <summary>
    /// Le cadre de la source choisie, découpé en dents contiguës tout autour. Une dent sur
    /// deux est peinte en sombre : c'est ce qui rend le cadre lisible sur n'importe quelle
    /// image, là où un trait d'une seule couleur disparaît dès que l'image a la même valeur.
    /// </summary>
    /// <remarks>
    /// Les dents sont rendues dans l'ordre du parcours, haut, droite, bas, gauche. Le tracé
    /// alterne les couleurs par leur rang.
    /// </remarks>
    internal static IReadOnlyList<PreviewFillRect> DashedFrame(
        double x,
        double y,
        double width,
        double height,
        float thickness,
        float dash)
    {
        if (width <= 0 || height <= 0 || thickness <= 0 || dash <= 0) return [];

        var left = (float)x;
        var top = (float)y;
        var boxWidth = (float)width;
        var boxHeight = (float)height;

        // Une source plus fine que deux traits est peinte pleine : deux bords qui se
        // recouvrent dessineraient un cadre plus épais que la source elle-même.
        if (boxWidth <= thickness * 2 || boxHeight <= thickness * 2)
            return [new PreviewFillRect(left, top, boxWidth, boxHeight)];

        var innerTop = top + thickness;
        var innerHeight = boxHeight - thickness * 2;
        var teeth = new List<PreviewFillRect>();

        AppendTeeth(teeth, left, top, boxWidth, thickness, dash, horizontal: true);
        AppendTeeth(teeth, left + boxWidth - thickness, innerTop, innerHeight, thickness, dash, horizontal: false);
        AppendTeeth(teeth, left, top + boxHeight - thickness, boxWidth, thickness, dash, horizontal: true);
        AppendTeeth(teeth, left, innerTop, innerHeight, thickness, dash, horizontal: false);
        return teeth;
    }

    private static void AppendTeeth(
        List<PreviewFillRect> into,
        float x,
        float y,
        float length,
        float thickness,
        float dash,
        bool horizontal)
    {
        for (var offset = 0f; offset < length; offset += dash)
        {
            var size = Math.Min(dash, length - offset);
            into.Add(horizontal
                ? new PreviewFillRect(x + offset, y, size, thickness)
                : new PreviewFillRect(x, y + offset, thickness, size));
        }
    }

    /// <summary>
    /// Les quatre équerres d'angle d'une source, deux branches chacune, tracées vers
    /// l'intérieur. Sur une source non choisie elles remplacent le cadre : marquer chaque
    /// source d'un cadre entier poserait un quadrillage sur l'image, qui est le sujet.
    /// </summary>
    /// <remarks>Rendues dans le sens des aiguilles d'une montre depuis le coin haut-gauche.</remarks>
    internal static IReadOnlyList<PreviewFillRect> CornerMarks(
        double x,
        double y,
        double width,
        double height,
        float thickness,
        float arm)
    {
        if (width <= 0 || height <= 0 || thickness <= 0 || arm <= 0) return [];

        var left = (float)x;
        var top = (float)y;
        var boxWidth = (float)width;
        var boxHeight = (float)height;
        var shortest = Math.Min(boxWidth, boxHeight);

        // Sur une petite source, quatre équerres pleine longueur se rejoindraient et
        // redessineraient le cadre qu'elles remplacent.
        var line = Math.Min(thickness, shortest / 2f);
        var branch = Math.Min(arm, shortest / 3f);
        var right = left + boxWidth;
        var bottom = top + boxHeight;

        return
        [
            new PreviewFillRect(left, top, branch, line),
            new PreviewFillRect(left, top, line, branch),
            new PreviewFillRect(right - branch, top, branch, line),
            new PreviewFillRect(right - line, top, line, branch),
            new PreviewFillRect(right - branch, bottom - line, branch, line),
            new PreviewFillRect(right - line, bottom - branch, line, branch),
            new PreviewFillRect(left, bottom - line, branch, line),
            new PreviewFillRect(left, bottom - branch, line, branch)
        ];
    }

    /// <summary>
    /// Les quatre barres au milieu des côtés de la source choisie. Une barre couchée sur son
    /// bord dit le geste qu'elle attend, là où un carré posé au même endroit ne dit rien.
    /// </summary>
    /// <remarks>Rendues dans l'ordre haut, droite, bas, gauche.</remarks>
    internal static IReadOnlyList<PreviewFillRect> EdgeMarks(
        double x,
        double y,
        double width,
        double height,
        float thickness,
        float bar)
    {
        if (width <= 0 || height <= 0 || thickness <= 0 || bar <= 0) return [];

        var left = (float)x;
        var top = (float)y;
        var boxWidth = (float)width;
        var boxHeight = (float)height;
        var line = Math.Min(thickness, Math.Min(boxWidth, boxHeight) / 2f);
        var horizontal = Math.Min(bar, boxWidth / 2f);
        var vertical = Math.Min(bar, boxHeight / 2f);
        var middleX = left + boxWidth / 2f - horizontal / 2f;
        var middleY = top + boxHeight / 2f - vertical / 2f;

        return
        [
            new PreviewFillRect(middleX, top, horizontal, line),
            new PreviewFillRect(left + boxWidth - line, middleY, line, vertical),
            new PreviewFillRect(middleX, top + boxHeight - line, horizontal, line),
            new PreviewFillRect(left, middleY, line, vertical)
        ];
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

            var selected = overlay.Selected;
            if (selected != null)
            {
                var teeth = DashedFrame(
                    selected.X, selected.Y, selected.Width, selected.Height,
                    metrics.DashThickness, metrics.Dash);

                SetColor(colorParameter, InkShadow);
                while (GsEffectLoop(effect, "Solid")) FillEveryOther(teeth, first: 1);

                SetColor(colorParameter, Ink);
                while (GsEffectLoop(effect, "Solid")) FillEveryOther(teeth, first: 0);
            }

            var marks = Marks(overlay, metrics);

            // Le fond sombre passe avant les marques : c'est ce qui les détache d'une image
            // claire, où une marque claire seule se perdrait.
            SetColor(colorParameter, InkShadow);
            while (GsEffectLoop(effect, "Solid")) FillAllGrown(marks, metrics.Backing);

            SetColor(colorParameter, Ink);
            while (GsEffectLoop(effect, "Solid")) FillAll(marks);
        }
        catch (Exception)
        {
            _outlinesUnavailable = true;
        }
    }

    // Une source choisie porte ses équerres et ses barres ; les autres, leurs seules équerres
    // d'angle. La différence se voit d'un coup d'œil, sans avoir à lire une couleur.
    private static List<PreviewFillRect> Marks(CompositionOverlay overlay, OverlayMetrics metrics)
    {
        var marks = new List<PreviewFillRect>();
        foreach (var source in overlay.Sources)
        {
            var chosen = overlay.Selected != null && source.SourceId == overlay.Selected.SourceId;
            marks.AddRange(CornerMarks(
                source.X, source.Y, source.Width, source.Height,
                chosen ? metrics.MarkThickness : metrics.IdleThickness,
                chosen ? metrics.MarkArm : metrics.IdleArm));

            if (chosen)
            {
                marks.AddRange(EdgeMarks(
                    source.X, source.Y, source.Width, source.Height,
                    metrics.MarkThickness, metrics.MarkBar));
            }
        }

        return marks;
    }

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

    private static void FillEveryOther(IReadOnlyList<PreviewFillRect> rects, int first)
    {
        for (var index = first; index < rects.Count; index += 2) Fill(rects[index]);
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
