using System.Runtime.InteropServices;
using CastorApplication.Models.Studio;
using LibObs;

namespace CastorApplication.Services.Studio;

internal readonly record struct PreviewViewport(int X, int Y, int Width, int Height);

/// <summary>Un rectangle plein à peindre, dans les coordonnées du canvas du moteur.</summary>
internal readonly record struct PreviewFillRect(float X, float Y, float Width, float Height);

internal static class ObsPreviewGraphics
{
    private const string ObsLibrary = "obs";

    // obs_base_effect : l'effet « solid » est le quatrième de l'énumération de libobs.
    private const int ObsEffectSolid = 3;

    // Épaisseur visée pour le trait des cadres, en pixels de l'écran. Elle est convertie en
    // pixels du canvas au moment du tracé : un cadre doit rester lisible que le panneau soit
    // large ou étroit, sans jamais déborder du rectangle qu'il entoure.
    private const float OutlineDeviceThickness = 2f;

    private static readonly Vec4 OutlineColor = new(0.357f, 0.553f, 0.937f, 1f);

    // Un symbole graphique absent ne doit pas emporter le thread de rendu de libobs : au
    // premier échec on renonce aux cadres pour de bon, l'image, elle, continue.
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

    /// <summary>
    /// Épaisseur de trait, en pixels du canvas, pour qu'un cadre fasse la même épaisseur à
    /// l'écran quelle que soit la taille à laquelle le canvas y est réduit.
    /// </summary>
    internal static float OutlineThickness(int viewportWidth, uint canvasWidth)
    {
        if (viewportWidth <= 0 || canvasWidth == 0) return OutlineDeviceThickness;

        return Math.Max(1f, OutlineDeviceThickness * canvasWidth / viewportWidth);
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
    /// Peint la scène puis, par-dessus, le cadre de chaque source composée. Les deux passent
    /// par la même projection : les cadres tombent exactement sur l'image, au pixel près et
    /// à la même image que le moteur.
    /// </summary>
    internal static void RenderScene(
        ObsDisplayFrame frame,
        ObsSource sceneSource,
        uint canvasWidth,
        uint canvasHeight,
        IReadOnlyList<SourceTransform> outlines)
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
                DrawOutlines(outlines, OutlineThickness(viewport.Width, canvasWidth));
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

    private static void DrawOutlines(IReadOnlyList<SourceTransform> outlines, float thickness)
    {
        if (outlines.Count == 0 || _outlinesUnavailable) return;

        try
        {
            var effect = ObsGetBaseEffect(ObsEffectSolid);
            if (effect == IntPtr.Zero) return;

            var colorParameter = GsEffectGetParamByName(effect, "color");
            if (colorParameter == IntPtr.Zero) return;

            var color = OutlineColor;
            GsEffectSetVec4(colorParameter, ref color);
            while (GsEffectLoop(effect, "Solid"))
            {
                foreach (var source in outlines)
                {
                    foreach (var edge in BoxEdges(source.X, source.Y, source.Width, source.Height, thickness))
                        Fill(edge);
                }
            }
        }
        catch (Exception)
        {
            _outlinesUnavailable = true;
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
