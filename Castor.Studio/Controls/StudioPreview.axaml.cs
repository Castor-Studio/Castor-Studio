using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using CastorApplication.Services.Studio;
using CastorApplication.ViewModels.Scenes;

namespace CastorApplication.Controls;

public partial class StudioPreview : UserControl
{
    public static readonly StyledProperty<SceneItemViewModel?> SceneProperty =
        AvaloniaProperty.Register<StudioPreview, SceneItemViewModel?>(nameof(Scene));

    public static readonly StyledProperty<string> MessageProperty =
        AvaloniaProperty.Register<StudioPreview, string>(nameof(Message), "LibObs n'est pas encore connecté.");

    public static readonly StyledProperty<IScenePreviewRuntime?> PreviewRuntimeProperty =
        AvaloniaProperty.Register<StudioPreview, IScenePreviewRuntime?>(nameof(PreviewRuntime));

    /// <summary>
    /// Set it to have the engine outline every composed source on the picture. Left unset,
    /// the preview shows the picture alone.
    /// </summary>
    public static readonly StyledProperty<SceneCompositionViewModel?> CompositionProperty =
        AvaloniaProperty.Register<StudioPreview, SceneCompositionViewModel?>(nameof(Composition));

    public static readonly StyledProperty<int> BaseCanvasWidthProperty =
        AvaloniaProperty.Register<StudioPreview, int>(nameof(BaseCanvasWidth), 1920);

    public static readonly StyledProperty<int> BaseCanvasHeightProperty =
        AvaloniaProperty.Register<StudioPreview, int>(nameof(BaseCanvasHeight), 1080);

    /// <summary>
    /// Whether the scene name is drawn over the picture. Off where the host already
    /// names the scene, so the name is not written twice on the same tile.
    /// </summary>
    public static readonly StyledProperty<bool> ShowSceneNameProperty =
        AvaloniaProperty.Register<StudioPreview, bool>(nameof(ShowSceneName), true);

    public bool ShowSceneName
    {
        get => GetValue(ShowSceneNameProperty);
        set => SetValue(ShowSceneNameProperty, value);
    }

    public SceneItemViewModel? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public string Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public IScenePreviewRuntime? PreviewRuntime
    {
        get => GetValue(PreviewRuntimeProperty);
        set => SetValue(PreviewRuntimeProperty, value);
    }

    public SceneCompositionViewModel? Composition
    {
        get => GetValue(CompositionProperty);
        set => SetValue(CompositionProperty, value);
    }

    public int BaseCanvasWidth
    {
        get => GetValue(BaseCanvasWidthProperty);
        set => SetValue(BaseCanvasWidthProperty, value);
    }

    public int BaseCanvasHeight
    {
        get => GetValue(BaseCanvasHeightProperty);
        set => SetValue(BaseCanvasHeightProperty, value);
    }

    // Demi-côté de la zone de prise d'une poignée, en pixels de l'écran : les poignées sont
    // peintes sur 10 px, on les saisit d'un peu plus loin.
    private const double HandleReach = 7;

    private static readonly Cursor SizeAllCursor = new(StandardCursorType.SizeAll);
    private static readonly Cursor TopLeftCursor = new(StandardCursorType.TopLeftCorner);
    private static readonly Cursor TopRightCursor = new(StandardCursorType.TopRightCorner);
    private static readonly Cursor BottomRightCursor = new(StandardCursorType.BottomRightCorner);
    private static readonly Cursor BottomLeftCursor = new(StandardCursorType.BottomLeftCorner);
    private static readonly Cursor TopCursor = new(StandardCursorType.TopSide);
    private static readonly Cursor BottomCursor = new(StandardCursorType.BottomSide);
    private static readonly Cursor LeftCursor = new(StandardCursorType.LeftSide);
    private static readonly Cursor RightCursor = new(StandardCursorType.RightSide);

    private readonly DispatcherTimer _flushTimer;

    public StudioPreview()
    {
        InitializeComponent();
        _flushTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = SceneCompositionViewModel.WriteInterval
        };
        _flushTimer.Tick += OnFlushTick;
        // Quitter la vue au milieu d'un geste ne doit pas laisser une source à mi-chemin.
        DetachedFromVisualTree += (_, _) => CancelGesture();
        SizeChanged += (_, _) => UpdatePreviewViewport();
        PropertyChanged += (_, change) =>
        {
            if (change.Property == BaseCanvasWidthProperty || change.Property == BaseCanvasHeightProperty)
                UpdatePreviewViewport();
        };
        UpdatePreviewViewport();
    }

    /// <summary>
    /// Ramène le clic dans le repère du canvas et y commence un geste : sur une poignée de
    /// la source choisie, l'étirer — ou la rogner, Alt enfoncé ; sur une source, la choisir
    /// et la déplacer. Sans composition, la vue ne fait que montrer : un clic n'y saisit rien.
    /// </summary>
    private void OnPicturePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var composition = Composition;
        if (composition == null) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (!TryGetCanvasScale(out _, out _)) return;

        var point = e.GetPosition(this);
        var target = NativePreview.Bounds.Contains(point) && ToCanvas(point) is var (x, y)
            ? composition.BeginGesture(x, y, HandleTolerance, crop: IsCrop(e.KeyModifiers))
            : null;

        if (target == null)
        {
            // Cliquer à côté de l'image ou de toute source, c'est ne viser aucune source.
            composition.ClearSelection();
        }
        else
        {
            // Capturé, le geste continue même quand le pointeur sort de l'image : une
            // source se pousse en partie hors du canvas, et un relâcher hors du panneau
            // termine quand même ce qu'il a commencé.
            e.Pointer.Capture(Picture);
            Focus();
            _flushTimer.Start();
        }

        NativePreview.ShowComposition();
        e.Handled = true;
    }

    private void OnPicturePointerMoved(object? sender, PointerEventArgs e)
    {
        var composition = Composition;
        if (composition == null) return;

        var point = e.GetPosition(this);
        if (ToCanvas(point) is not var (x, y)) return;

        if (!composition.IsGesturing)
        {
            // Au survol, le curseur annonce ce qu'un clic saisirait.
            Picture.Cursor = NativePreview.Bounds.Contains(point)
                ? CursorFor(composition.TargetAt(x, y, HandleTolerance, IsCrop(e.KeyModifiers)))
                : null;
            return;
        }

        // Maj libère les proportions d'un angle, comme partout ailleurs.
        composition.UpdateGesture(x, y, keepAspectRatio: !e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        NativePreview.ShowComposition();
        e.Handled = true;
    }

    private void OnPicturePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var composition = Composition;
        if (composition is not { IsGesturing: true }) return;

        _flushTimer.Stop();
        composition.EndGesture();
        e.Pointer.Capture(null);
        NativePreview.ShowComposition();
        e.Handled = true;
    }

    // Une capture perdue sans relâcher (une fenêtre qui passe devant, un Alt+Tab) ne sait
    // pas où l'opérateur voulait finir : la source reprend son placement d'avant le geste.
    private void OnPicturePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => CancelGesture();

    private void OnPictureKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || Composition is not { IsGesturing: true }) return;

        CancelGesture();
        e.Handled = true;
    }

    private void CancelGesture()
    {
        var composition = Composition;
        if (composition is not { IsGesturing: true }) return;

        _flushTimer.Stop();
        composition.CancelGesture();
        NativePreview.ShowComposition();
    }

    // Rattrape un pointeur qui s'arrête entre deux écritures : sans lui, la dernière
    // position attendrait le mouvement suivant ou le relâcher pour atteindre le moteur.
    private void OnFlushTick(object? sender, EventArgs e)
    {
        var composition = Composition;
        if (composition is not { IsGesturing: true })
        {
            _flushTimer.Stop();
            return;
        }

        composition.FlushGesture();
        // Un refus pendant le rattrapage arrête le geste : l'overlay revient à ce que le
        // moteur détient.
        if (!composition.IsGesturing) _flushTimer.Stop();
        NativePreview.ShowComposition();
    }

    // L'image occupe exactement le rectangle de la surface native : un point de l'écran s'y
    // ramène par le seul rapport des deux tailles. C'est le changement de repère du moteur,
    // pris à l'envers, et le seul endroit où il est écrit.
    private (double X, double Y)? ToCanvas(Point point)
    {
        if (!TryGetCanvasScale(out var scaleX, out var scaleY)) return null;

        var picture = NativePreview.Bounds;
        return ((point.X - picture.X) * scaleX, (point.Y - picture.Y) * scaleY);
    }

    private bool TryGetCanvasScale(out double scaleX, out double scaleY)
    {
        var picture = NativePreview.Bounds;
        if (picture.Width <= 0 || picture.Height <= 0 || BaseCanvasWidth <= 0 || BaseCanvasHeight <= 0)
        {
            scaleX = scaleY = 0;
            return false;
        }

        scaleX = BaseCanvasWidth / picture.Width;
        scaleY = BaseCanvasHeight / picture.Height;
        return true;
    }

    // Une poignée se vise à la souris : sa zone de prise est pensée en pixels de l'écran,
    // un peu plus large que le carré peint, puis convertie dans le repère du canvas.
    private double HandleTolerance => TryGetCanvasScale(out var scaleX, out _) ? HandleReach * scaleX : 0;

    private static bool IsCrop(KeyModifiers modifiers) => modifiers.HasFlag(KeyModifiers.Alt);

    private static Cursor? CursorFor(CompositionTarget? target) => target switch
    {
        null => null,
        { Kind: CompositionGestureKind.Move } => SizeAllCursor,
        { Handle: CompositionHandle.TopLeft } => TopLeftCursor,
        { Handle: CompositionHandle.TopRight } => TopRightCursor,
        { Handle: CompositionHandle.BottomRight } => BottomRightCursor,
        { Handle: CompositionHandle.BottomLeft } => BottomLeftCursor,
        { Handle: CompositionHandle.Top } => TopCursor,
        { Handle: CompositionHandle.Bottom } => BottomCursor,
        { Handle: CompositionHandle.Left } => LeftCursor,
        _ => RightCursor
    };

    private void UpdatePreviewViewport()
    {
        if (BaseCanvasWidth <= 0 || BaseCanvasHeight <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var scale = Math.Min(Bounds.Width / BaseCanvasWidth, Bounds.Height / BaseCanvasHeight);
        NativePreview.Width = Math.Max(1, Math.Round(BaseCanvasWidth * scale));
        NativePreview.Height = Math.Max(1, Math.Round(BaseCanvasHeight * scale));
    }
}
