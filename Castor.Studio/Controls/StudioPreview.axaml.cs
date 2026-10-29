using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
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

    // Distance, en pixels de l'écran, à laquelle une source déplacée s'accroche à un bord ou
    // un centre : assez pour qu'on la sente, assez peu pour pouvoir passer à côté.
    private const double SnapReach = 8;

    private static readonly Cursor SizeAllCursor = new(StandardCursorType.SizeAll);
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor AimCursor = new(StandardCursorType.Cross);
    // Les quatre axes d'étirement : ↔, ↘↖, ↕, ↗↙.
    private static readonly Cursor HorizontalCursor = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor DescendingDiagonalCursor = new(StandardCursorType.TopLeftCorner);
    private static readonly Cursor VerticalCursor = new(StandardCursorType.SizeNorthSouth);
    private static readonly Cursor AscendingDiagonalCursor = new(StandardCursorType.TopRightCorner);
    private static Cursor? _rotateCursor;

    private readonly DispatcherTimer _flushTimer;
    private ContextMenu? _sourceMenu;

    public StudioPreview()
    {
        InitializeComponent();
        _flushTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = SceneCompositionViewModel.WriteInterval
        };
        _flushTimer.Tick += OnFlushTick;
        // Quitter la vue au milieu d'un geste ne doit pas laisser une source à mi-chemin.
        // Le menu de la source, lui, n'a plus rien à viser.
        DetachedFromVisualTree += (_, _) =>
        {
            CancelGesture();
            CloseSourceMenu();
        };
        // L'image se cale dans la place que la barre lui laisse, pas dans tout le contrôle.
        Picture.SizeChanged += (_, _) => UpdatePreviewViewport();
        DescribeTools();
        PropertyChanged += (_, change) =>
        {
            if (change.Property == BaseCanvasWidthProperty || change.Property == BaseCanvasHeightProperty)
                UpdatePreviewViewport();
        };
        UpdatePreviewViewport();

        // Écoutés en descente, et même déjà traités : les flèches et Alt servent ailleurs à
        // la navigation au clavier, qui les prendrait avant qu'ils ne remontent jusqu'ici.
        AddHandler(KeyDownEvent, OnPictureKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyUpEvent, OnPictureKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);

        // Les pastilles posées sur l'image sont rendues ici, à l'échelle de cet écran.
        AttachedToVisualTree += (_, _) => PreviewBadgeRenderer.Attach(TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
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
        if (ToCanvas(e.GetPosition(Picture)) is not var (x, y)) return;

        // Tout clic sur l'aperçu lui donne le clavier : c'est là que zoom et flèches s'écoutent.
        // Il ferme aussi le menu resté ouvert, comme partout ailleurs.
        Focus();
        CloseSourceMenu();

        var properties = e.GetCurrentPoint(this).Properties;
        var onPicture = NativePreview.Bounds.Contains(e.GetPosition(Picture));

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
            ? composition.BeginGesture(x, y, HandleTolerance, IsCrop(e.KeyModifiers), RotationTolerance, CanvasPerScreenPixel)
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

        var point = e.GetPosition(Picture);
        if (ToCanvas(point) is not var (x, y)) return;

        // Alt peut s'enfoncer pendant que la page n'a pas le focus clavier : le pointeur le
        // relit à chaque mouvement.
        UpdateCropModifier(composition, e.KeyModifiers);

        if (!composition.IsGesturing)
        {
            // Au survol, le curseur annonce ce qu'un clic saisirait, et la source visée se
            // montre au poids qu'elle aura une fois prise.
            var onPicture = NativePreview.Bounds.Contains(point);
            Picture.Cursor = onPicture
                ? CursorFor(
                    composition.TargetAt(x, y, HandleTolerance, IsCrop(e.KeyModifiers), RotationTolerance, CanvasPerScreenPixel),
                    composition.Selected?.Rotation ?? 0)
                : null;
            if (onPicture ? composition.HoverAt(x, y) : composition.ClearHover())
                NativePreview.ShowComposition();
            return;
        }

        // Maj libère les proportions d'un angle, et contraint une rotation au pas de 15°.
        // Ctrl coupe l'aimant, pour poser une source au pixel près d'un bord.
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var snap = e.KeyModifiers.HasFlag(KeyModifiers.Control) ? 0 : SnapTolerance;
        composition.UpdateGesture(x, y, keepAspectRatio: !shift, constrainRotation: shift, snapReach: snap);
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

    // Alt + molette zoome l'image de la source visée vers le pointeur ; sans Alt ni mode
    // rognage, la molette ne fait rien ici.
    private void OnPicturePointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var composition = Composition;
        if (composition == null) return;

        var point = e.GetPosition(Picture);
        if (!NativePreview.Bounds.Contains(point) || ToCanvas(point) is not var (x, y)) return;

        UpdateCropModifier(composition, e.KeyModifiers);
        if (!composition.ZoomAt(x, y, e.Delta.Y)) return;

        Focus();
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

        // Échap défait une chose à la fois : le geste en cours, puis le rognage, puis la
        // sélection. Sans rien de tout ça, il laisse passer la touche à qui l'attend.
        switch (e.Key)
        {
            case Key.Escape when composition.IsGesturing:
                CancelGesture();
                e.Handled = true;
                return;
            case Key.Escape or Key.Enter when composition.IsCropMode:
                composition.ExitCropMode();
                NativePreview.ShowComposition();
                e.Handled = true;
                return;
            case Key.Escape when composition.HasSelection:
                composition.ClearSelection();
                NativePreview.ShowComposition();
                e.Handled = true;
                return;
        }

        // Une commande au milieu d'un geste écrirait par-dessus ce que le pointeur demande.
        if (composition.IsGesturing) return;
        if (CompositionShortcuts.Resolve(e.Key, e.KeyModifiers, e.KeySymbol) is not { } action) return;

        e.Handled = Execute(composition, action);
    }

    // Touche, bouton de la barre et ligne du menu passent tous par la composition : c'est ce
    // qui leur garantit le même effet. L'aperçu montre aussitôt ce qui en résulte.
    private bool Execute(SceneCompositionViewModel composition, CompositionKeyAction action)
    {
        var done = composition.Execute(action);
        NativePreview.ShowComposition();
        return done;
    }

    // Les boutons de la barre portent leur commande dans Tag. Ils ne prennent pas le
    // clavier : l'aperçu le garde, et les raccourcis continuent de répondre après un clic.
    private void OnToolClick(object? sender, RoutedEventArgs e)
    {
        if (Composition is not { } composition || sender is not Control { Tag: CompositionCommand command }) return;

        CloseSourceMenu();
        Execute(composition, new CompositionKeyAction(command));
        Focus();
    }

    // Chaque outil de la barre prend l'icône que sa commande porte dans le menu, devant son
    // libellé s'il en a un ; sa bulle dit aussi la touche qui fait la même chose. Un outil
    // qui montre déjà autre chose (le facteur du zoom) le garde.
    private void DescribeTools()
    {
        foreach (var tool in Toolbar.GetLogicalDescendants().OfType<Button>().ToList())
        {
            if (tool.Tag is not CompositionCommand command) continue;

            if (ToolTip.GetTip(tool) is string tip)
                ToolTip.SetTip(tool, CompositionShortcuts.Describe(tip, command));

            var icon = SourceMenu.Icon(CompositionIcons.Of(command), "tool-icon", 15);
            tool.Content = tool.Content switch
            {
                string label => new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = { icon, new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center } }
                },
                null => icon,
                var content => content
            };
        }
    }

    // Le curseur écrit son zoom par la composition (liaison) ; il reste à montrer au moteur
    // l'overlay qui en découle, la mini-carte d'une source zoomée comprise.
    private void OnZoomSliderChanged(object? sender, RangeBaseValueChangedEventArgs e) =>
        NativePreview.ShowComposition();

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

    /// <summary>
    /// Ce que la page ajoute au menu d'une source, en dernier groupe : renommer, retirer…
    /// Non renseigné, le menu ne porte que la composition.
    /// </summary>
    public Func<Guid, IEnumerable<Control>>? SourceActions { get; set; }

    // Le menu de la source choisie : le même qu'au clic droit sur sa ligne dans la liste.
    private void OpenSourceMenu(SceneCompositionViewModel composition)
    {
        var sourceId = composition.Selected!.SourceId;
        var menu = SourceMenu.Create(
            composition,
            Scene?.Sources.FirstOrDefault(item => item.Id == sourceId),
            SourceActions?.Invoke(sourceId) ?? []);
        menu.Closed += (_, _) =>
        {
            if (_sourceMenu == menu) _sourceMenu = null;
        };
        _sourceMenu = menu;
        menu.Open(Picture);
    }

    // Le menu ouvert sur la surface native ne se ferme pas de lui-même quand on clique
    // ailleurs dans l'aperçu : le clic n'atteint jamais sa fenêtre. On le ferme donc ici,
    // avant d'en ouvrir un autre ou de commencer quoi que ce soit d'autre.
    private void CloseSourceMenu()
    {
        var menu = _sourceMenu;
        _sourceMenu = null;
        menu?.Close();
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

    private double SnapTolerance => TryGetCanvasScale(out var scaleX, out _) ? SnapReach * scaleX : 0;

    // La mini-carte se pense en pixels physiques de l'écran, comme le moteur la peint dans sa
    // surface native : elle tombe ainsi au même endroit pour le clic et pour l'œil, quelle
    // que soit la mise à l'échelle de Windows.
    private double CanvasPerScreenPixel =>
        TryGetCanvasScale(out var scaleX, out _) ? scaleX / (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1) : 0;

    // Le pointeur quitte l'image : plus rien n'est survolé.
    private void OnPicturePointerExited(object? sender, PointerEventArgs e)
    {
        if (Composition is { } composition && composition.ClearHover()) NativePreview.ShowComposition();
    }

    private static bool IsCrop(KeyModifiers modifiers) => modifiers.HasFlag(KeyModifiers.Alt);

    private static bool IsOverSelected(SceneCompositionViewModel composition, double x, double y) =>
        composition.Selected is { } selected && CompositionGeometry.Contains(selected, x, y);

    private static Cursor? CursorFor(CompositionTarget? target, double rotation) => target switch
    {
        null => null,
        { Kind: CompositionGestureKind.Move } => SizeAllCursor,
        { Kind: CompositionGestureKind.Rotate } => RotateCursor,
        { Kind: CompositionGestureKind.Pan } => HandCursor,
        { Kind: CompositionGestureKind.Aim } => AimCursor,
        { Handle: { } handle } => PullCursor(CompositionGeometry.PullDirection(handle, rotation)),
        _ => null
    };

    // Une poignée tire dans la direction de son bord, qui tourne avec la source : le curseur
    // suit cette direction, ramenée au plus proche des quatre axes que Windows sait dessiner.
    private static Cursor PullCursor(double direction)
    {
        var axis = (int)Math.Round((direction + 360) % 180 / 45) % 4;
        return axis switch
        {
            0 => HorizontalCursor,
            1 => DescendingDiagonalCursor,
            2 => VerticalCursor,
            _ => AscendingDiagonalCursor
        };
    }

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
        var area = Picture.Bounds;
        if (BaseCanvasWidth <= 0 || BaseCanvasHeight <= 0 || area.Width <= 0 || area.Height <= 0)
            return;

        var scale = Math.Min(area.Width / BaseCanvasWidth, area.Height / BaseCanvasHeight);
        NativePreview.Width = Math.Max(1, Math.Round(BaseCanvasWidth * scale));
        NativePreview.Height = Math.Max(1, Math.Round(BaseCanvasHeight * scale));
    }
}
