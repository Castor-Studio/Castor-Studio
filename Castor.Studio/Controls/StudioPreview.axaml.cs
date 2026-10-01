using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CastorApplication.Models.Studio;
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

    // Jusqu'où, hors d'un coin de la source choisie, le pointeur la fait tourner, en pixels
    // de l'écran. Rien n'y est peint : c'est le curseur qui l'annonce.
    private const double RotationReach = 24;

    // Écart entre le pointeur et la bulle qui dit ce que le geste écrit, en pixels de
    // l'écran : assez pour ne pas cacher ce qu'on vise.
    private const double ReadoutOffset = 18;

    private static readonly Cursor SizeAllCursor = new(StandardCursorType.SizeAll);
    private static readonly Cursor TopLeftCursor = new(StandardCursorType.TopLeftCorner);
    private static readonly Cursor TopRightCursor = new(StandardCursorType.TopRightCorner);
    private static readonly Cursor BottomRightCursor = new(StandardCursorType.BottomRightCorner);
    private static readonly Cursor BottomLeftCursor = new(StandardCursorType.BottomLeftCorner);
    private static readonly Cursor TopCursor = new(StandardCursorType.TopSide);
    private static readonly Cursor BottomCursor = new(StandardCursorType.BottomSide);
    private static readonly Cursor LeftCursor = new(StandardCursorType.LeftSide);
    private static readonly Cursor RightCursor = new(StandardCursorType.RightSide);
    private static Cursor? _rotateCursor;

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
    /// la source choisie, l'étirer — ou la rogner, en mode rognage ou Alt enfoncé ; juste
    /// hors d'un de ses coins, la faire tourner ; sur une source, la choisir et la déplacer.
    /// Un double-clic sur la source choisie entre en mode rognage ou en sort ; un clic droit
    /// ouvre son menu. Sans composition, la vue ne fait que montrer : un clic n'y saisit rien.
    /// </summary>
    private void OnPicturePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var composition = Composition;
        if (composition == null) return;
        if (ToCanvas(e.GetPosition(this)) is not var (x, y)) return;

        var properties = e.GetCurrentPoint(this).Properties;
        var onPicture = NativePreview.Bounds.Contains(e.GetPosition(this));

        if (properties.IsRightButtonPressed)
        {
            if (composition.IsGesturing) return;

            // Le menu porte sur ce qui est sous le pointeur, comme partout ailleurs.
            if (onPicture) composition.SelectAt(x, y);
            else composition.ClearSelection();

            NativePreview.ShowComposition();
            if (composition.HasSelection) OpenSourceMenu(composition);
            e.Handled = true;
            return;
        }

        if (!properties.IsLeftButtonPressed) return;

        // Le premier clic a déjà choisi la source ; le second, sur elle, bascule le rognage.
        if (e.ClickCount == 2 && onPicture && IsOverSelected(composition, x, y))
        {
            composition.ToggleCropMode();
            NativePreview.ShowComposition();
            e.Handled = true;
            return;
        }

        var target = onPicture
            ? composition.BeginGesture(x, y, HandleTolerance, IsCrop(e.KeyModifiers), RotationTolerance)
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

        // Alt peut s'enfoncer pendant que la page n'a pas le focus clavier : le pointeur le
        // relit à chaque mouvement.
        UpdateCropModifier(composition, e.KeyModifiers);

        if (!composition.IsGesturing)
        {
            // Au survol, le curseur annonce ce qu'un clic saisirait.
            Picture.Cursor = NativePreview.Bounds.Contains(point)
                ? CursorFor(composition.TargetAt(x, y, HandleTolerance, IsCrop(e.KeyModifiers), RotationTolerance))
                : null;
            return;
        }

        // Maj libère les proportions d'un angle, et contraint une rotation au pas de 15°.
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        composition.UpdateGesture(x, y, keepAspectRatio: !shift, constrainRotation: shift);
        ShowReadout(composition, e.GetPosition(Picture));
        NativePreview.ShowComposition();
        e.Handled = true;
    }

    private void OnPicturePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var composition = Composition;
        if (composition is not { IsGesturing: true }) return;

        _flushTimer.Stop();
        Readout.IsOpen = false;
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
        var composition = Composition;
        if (composition == null) return;

        UpdateCropModifier(composition, e.KeyModifiers);

        switch (e.Key)
        {
            // Échap annule d'abord le geste en cours ; sans geste, il quitte le rognage.
            case Key.Escape when composition.IsGesturing:
                CancelGesture();
                break;
            case Key.Escape or Key.Enter when composition.IsCropMode:
                composition.ExitCropMode();
                NativePreview.ShowComposition();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnPictureKeyUp(object? sender, KeyEventArgs e)
    {
        if (Composition is { } composition) UpdateCropModifier(composition, e.KeyModifiers);
    }

    // Tant qu'Alt est enfoncé, la source choisie se montre en rognage ; le moteur n'en est
    // prévenu que si l'apparence change vraiment.
    private void UpdateCropModifier(SceneCompositionViewModel composition, KeyModifiers modifiers)
    {
        var wasCropping = composition.IsCropping;
        composition.SetCropModifier(IsCrop(modifiers));
        if (composition.IsCropping != wasCropping) NativePreview.ShowComposition();
    }

    private void CancelGesture()
    {
        var composition = Composition;
        if (composition is not { IsGesturing: true }) return;

        _flushTimer.Stop();
        Readout.IsOpen = false;
        composition.CancelGesture();
        NativePreview.ShowComposition();
    }

    // La bulle est une fenêtre à elle : c'est ce qui la laisse passer au-dessus de la
    // surface native, où rien de ce que dessine la page ne peut se poser.
    private void ShowReadout(SceneCompositionViewModel composition, Point pointer)
    {
        var text = composition.GestureReadout;
        if (text.Length == 0)
        {
            Readout.IsOpen = false;
            return;
        }

        ReadoutText.Text = text;
        Readout.PlacementRect = new Rect(pointer.X + ReadoutOffset, pointer.Y + ReadoutOffset, 1, 1);
        Readout.IsOpen = true;
    }

    // Le menu de la source choisie. Ses commandes passent par le moteur comme un geste : un
    // refus se dit sous la liste des sources, et l'aperçu revient à ce que le moteur détient.
    private void OpenSourceMenu(SceneCompositionViewModel composition)
    {
        var selected = composition.Selected!;

        MenuItem Item(string header, Action action, bool isEnabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = isEnabled };
            item.Click += (_, _) =>
            {
                action();
                NativePreview.ShowComposition();
            };
            return item;
        }

        var menu = new ContextMenu
        {
            Items =
            {
                Item(composition.IsCropMode ? "Terminer le rognage" : "Rogner", composition.ToggleCropMode),
                Item("Réinitialiser le rognage", composition.ResetSelectedCrop, selected.Crop != SourceCrop.None),
                new Separator(),
                Item("Pivoter de 90° à droite", () => composition.RotateSelected(90)),
                Item("Pivoter de 90° à gauche", () => composition.RotateSelected(-90)),
                Item("Pivoter de 180°", () => composition.RotateSelected(180)),
                Item("Réinitialiser la rotation", composition.ResetSelectedRotation, selected.Rotation != 0)
            }
        };
        menu.Open(Picture);
    }

    // Rattrape un pointeur qui s'arrête entre deux écritures : sans lui, la dernière
    // position attendrait le mouvement suivant ou le relâcher pour atteindre le moteur.
    private void OnFlushTick(object? sender, EventArgs e)
    {
        var composition = Composition;
        if (composition is { IsGesturing: true }) composition.FlushGesture();

        // Un refus pendant le rattrapage arrête le geste : l'overlay revient à ce que le
        // moteur détient, et la bulle n'a plus rien à dire.
        if (composition is not { IsGesturing: true })
        {
            _flushTimer.Stop();
            Readout.IsOpen = false;
        }

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
    // un peu plus large que le carré peint, puis convertie dans le repère du canvas. La zone
    // de rotation, de même.
    private double HandleTolerance => TryGetCanvasScale(out var scaleX, out _) ? HandleReach * scaleX : 0;

    private double RotationTolerance => TryGetCanvasScale(out var scaleX, out _) ? RotationReach * scaleX : 0;

    private static bool IsCrop(KeyModifiers modifiers) => modifiers.HasFlag(KeyModifiers.Alt);

    private static bool IsOverSelected(SceneCompositionViewModel composition, double x, double y) =>
        composition.Selected is { } selected && CompositionGeometry.Contains(selected, x, y);

    private static Cursor? CursorFor(CompositionTarget? target) => target switch
    {
        null => null,
        { Kind: CompositionGestureKind.Move } => SizeAllCursor,
        { Kind: CompositionGestureKind.Rotate } => RotateCursor,
        { Handle: CompositionHandle.TopLeft } => TopLeftCursor,
        { Handle: CompositionHandle.TopRight } => TopRightCursor,
        { Handle: CompositionHandle.BottomRight } => BottomRightCursor,
        { Handle: CompositionHandle.BottomLeft } => BottomLeftCursor,
        { Handle: CompositionHandle.Top } => TopCursor,
        { Handle: CompositionHandle.Bottom } => BottomCursor,
        { Handle: CompositionHandle.Left } => LeftCursor,
        _ => RightCursor
    };

    // Windows n'a pas de curseur de rotation : il est dessiné une fois, une flèche en arc
    // claire sur un liseré sombre, lisible sur n'importe quelle image.
    private static Cursor RotateCursor => _rotateCursor ??= CreateRotateCursor();

    private static Cursor CreateRotateCursor()
    {
        const int size = 24;
        var arrow = Geometry.Parse("M 6,14 A 7,7 0 1 1 13,19 M 13,19 L 9,17 M 13,19 L 11,15");
        var bitmap = new RenderTargetBitmap(new PixelSize(size, size));
        using (var context = bitmap.CreateDrawingContext())
        {
            context.DrawGeometry(null, new Pen(Brushes.Black, 4, lineCap: PenLineCap.Round), arrow);
            context.DrawGeometry(null, new Pen(Brushes.White, 2, lineCap: PenLineCap.Round), arrow);
        }

        return new Cursor(bitmap, new PixelPoint(size / 2, size / 2));
    }

    private void UpdatePreviewViewport()
    {
        if (BaseCanvasWidth <= 0 || BaseCanvasHeight <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var scale = Math.Min(Bounds.Width / BaseCanvasWidth, Bounds.Height / BaseCanvasHeight);
        NativePreview.Width = Math.Max(1, Math.Round(BaseCanvasWidth * scale));
        NativePreview.Height = Math.Max(1, Math.Round(BaseCanvasHeight * scale));
    }
}
