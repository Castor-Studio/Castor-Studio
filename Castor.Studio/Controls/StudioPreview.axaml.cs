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

    /// <summary>Whether a thin frame marks the edges of the canvas.</summary>
    public static readonly StyledProperty<bool> ShowCanvasFrameProperty =
        AvaloniaProperty.Register<StudioPreview, bool>(nameof(ShowCanvasFrame));

    /// <summary>
    /// Shows the canvas pixel for pixel rather than fitted, as far as the area allows: the
    /// native surface cannot be clipped by the page, so it never grows past the area.
    /// </summary>
    public static readonly StyledProperty<bool> IsActualSizeProperty =
        AvaloniaProperty.Register<StudioPreview, bool>(nameof(IsActualSize));

    public bool ShowCanvasFrame
    {
        get => GetValue(ShowCanvasFrameProperty);
        set => SetValue(ShowCanvasFrameProperty, value);
    }

    public bool IsActualSize
    {
        get => GetValue(IsActualSizeProperty);
        set => SetValue(IsActualSizeProperty, value);
    }

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

    // Half-side of a handle's grab area, in screen pixels: handles are painted at 10 px and can
    // be grabbed from a little further away.
    private const double HandleReach = 7;

    // How far outside a corner of the selected source the pointer rotates it, in screen pixels.
    // Nothing is painted there: the cursor announces it.
    private const double RotationReach = 24;

    // Gap between the pointer and the bubble saying what the gesture writes, in screen pixels:
    // enough not to hide the target.
    private const double ReadoutOffset = 18;

    // Distance, in screen pixels, at which a moved source snaps to an edge or center: enough to
    // feel it, little enough to be able to pass by.
    private const double SnapReach = 8;

    private static readonly Cursor SizeAllCursor = new(StandardCursorType.SizeAll);
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor AimCursor = new(StandardCursorType.Cross);
    // The four stretch axes: ↔, ↘↖, ↕, ↗↙.
    private static readonly Cursor HorizontalCursor = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor DescendingDiagonalCursor = new(StandardCursorType.TopLeftCorner);
    private static readonly Cursor VerticalCursor = new(StandardCursorType.SizeNorthSouth);
    private static readonly Cursor AscendingDiagonalCursor = new(StandardCursorType.TopRightCorner);
    private static Cursor? _rotateCursor;

    // How far one wheel notch moves an actual-size canvas, in page units.
    private const double WheelPanStep = 60;

    private Rect? _pictureArea;
    private Vector _pan;
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
        // Leaving the view mid-gesture must not leave a source halfway. The source menu, for its
        // part, has nothing left to target.
        DetachedFromVisualTree += (_, _) =>
        {
            CancelGesture();
            CloseSourceMenu();
        };
        // The picture fits the room the toolbar leaves it, not the whole control.
        Picture.SizeChanged += (_, _) => UpdatePreviewViewport();
        DescribeTools();
        PropertyChanged += (_, change) =>
        {
            if (change.Property == BaseCanvasWidthProperty || change.Property == BaseCanvasHeightProperty
                || change.Property == IsActualSizeProperty)
                UpdatePreviewViewport();
        };
        UpdatePreviewViewport();

        // Listened to while tunneling, even when already handled: arrows and Alt serve keyboard
        // navigation elsewhere, which would take them before they bubble up here.
        AddHandler(KeyDownEvent, OnPictureKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyUpEvent, OnPictureKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);

        // Badges placed on the picture are rendered here, at this screen's scale.
        AttachedToVisualTree += (_, _) => PreviewBadgeRenderer.Attach(TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
    }

    /// <summary>
    /// Brings the click into canvas coordinates and starts a gesture there: on a handle of the
    /// selected source, stretch it, or crop it in crop mode or with Alt held; just outside one
    /// of its corners, rotate it; on a source, select and move it. A double-click on the selected
    /// source enters or leaves crop mode; a right-click opens its menu. Without a composition,
    /// the view only shows: a click grabs nothing.
    /// </summary>
    private void OnPicturePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var composition = Composition;
        if (composition == null) return;
        if (ToCanvas(e.GetPosition(Picture)) is not var (x, y)) return;

        // Any click on the preview gives it the keyboard: that is where zoom and arrows are heard.
        // It also closes a menu left open, as everywhere else.
        Focus();
        CloseSourceMenu();

        var properties = e.GetCurrentPoint(this).Properties;
        var onPicture = IsOnPicture(e.GetPosition(Picture));

        if (properties.IsRightButtonPressed)
        {
            if (composition.IsGesturing) return;

            // The menu applies to what is under the pointer, as everywhere else.
            if (onPicture) composition.SelectAt(x, y);
            else composition.ClearSelection();

            NativePreview.ShowComposition();
            if (composition.HasSelection) OpenSourceMenu(composition);
            e.Handled = true;
            return;
        }

        if (!properties.IsLeftButtonPressed) return;

        // The first click already selected the source; the second, on it, toggles crop.
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
            // Clicking beside the picture or any source means targeting no source.
            composition.ClearSelection();
        }
        else
        {
            // Captured, the gesture goes on even when the pointer leaves the picture: a source can be
            // pushed partly off the canvas, and a release outside the panel still ends what it started.
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

        // Alt may go down while the page does not have keyboard focus: the pointer reads it again
        // on every move.
        UpdateCropModifier(composition, e.KeyModifiers);

        if (!composition.IsGesturing)
        {
            // On hover, the cursor announces what a click would grab, and the targeted source
            // shows with the weight it will have once taken.
            var onPicture = IsOnPicture(point);
            Picture.Cursor = onPicture
                ? CursorFor(
                    composition.TargetAt(x, y, HandleTolerance, IsCrop(e.KeyModifiers), RotationTolerance, CanvasPerScreenPixel),
                    composition.Selected?.Rotation ?? 0)
                : null;
            if (onPicture ? composition.HoverAt(x, y) : composition.ClearHover())
                NativePreview.ShowComposition();
            return;
        }

        // Shift frees a corner's proportions, and constrains a rotation to the 15° step.
        // Ctrl turns snapping off, to place a source to the pixel near an edge.
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

    // Alt + wheel zooms the targeted source's image towards the pointer; without Alt or crop
    // mode, the wheel does nothing here.
    private void OnPicturePointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var composition = Composition;
        if (composition == null) return;

        var point = e.GetPosition(Picture);
        if (!IsOnPicture(point) || ToCanvas(point) is not var (x, y)) return;

        UpdateCropModifier(composition, e.KeyModifiers);
        if (!composition.ZoomAt(x, y, e.Delta.Y))
        {
            // Outside cropping, the wheel pans a canvas larger than the area; with Shift,
            // left to right.
            if (_pictureArea == null) return;

            var horizontal = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            _pan += new Vector(
                (horizontal ? e.Delta.Y : e.Delta.X) * WheelPanStep,
                (horizontal ? 0 : e.Delta.Y) * WheelPanStep);
            UpdatePreviewViewport();
            e.Handled = true;
            return;
        }

        Focus();
        NativePreview.ShowComposition();
        e.Handled = true;
    }

    // A capture lost without a release (a window coming to the front, Alt+Tab) cannot tell where
    // the operator meant to finish: the source returns to its pre-gesture placement.
    private void OnPicturePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => CancelGesture();

    private void OnPictureKeyDown(object? sender, KeyEventArgs e)
    {
        var composition = Composition;
        if (composition == null) return;

        UpdateCropModifier(composition, e.KeyModifiers);

        // Escape undoes one thing at a time: the current gesture, then the crop, then the
        // selection. With none of those, the key goes on to whoever is waiting for it.
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

        // A command in the middle of a gesture would overwrite what the pointer is asking for.
        if (composition.IsGesturing) return;
        if (CompositionShortcuts.Resolve(e.Key, e.KeyModifiers, e.KeySymbol) is not { } action) return;

        e.Handled = Execute(composition, action);
    }

    // Keys, toolbar buttons and menu rows all go through the composition, which guarantees
    // them the same effect. The preview shows the result at once.
    private bool Execute(SceneCompositionViewModel composition, CompositionKeyAction action)
    {
        var done = composition.Execute(action);
        NativePreview.ShowComposition();
        return done;
    }

    // Toolbar buttons carry their command in Tag. They do not take the keyboard: the preview
    // keeps it, so shortcuts still answer after a click.
    private void OnToolClick(object? sender, RoutedEventArgs e)
    {
        if (Composition is not { } composition || sender is not Control { Tag: CompositionCommand command }) return;

        CloseSourceMenu();
        Execute(composition, new CompositionKeyAction(command));
        Focus();
    }

    // Each toolbar tool takes the icon its command has in the menu, before its label if it has
    // one; its tooltip also names the key that does the same. A tool already showing something
    // else (the zoom factor) keeps it.
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

    // The slider writes its zoom through the composition (binding); what is left is to show the
    // engine the resulting overlay, a zoomed source's mini-map included.
    private void OnZoomSliderChanged(object? sender, RangeBaseValueChangedEventArgs e) =>
        NativePreview.ShowComposition();

    private void OnPictureKeyUp(object? sender, KeyEventArgs e)
    {
        if (Composition is { } composition) UpdateCropModifier(composition, e.KeyModifiers);
    }

    // While Alt is held, the selected source shows as cropping; the engine is only told if the
    // appearance really changes.
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

    // The bubble is a window of its own: that is what lets it pass over the native surface,
    // where nothing the page draws can sit.
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
    /// What the page adds to a source's menu, as the last group: rename, remove…
    /// Left unset, the menu carries the composition commands only.
    /// </summary>
    public Func<Guid, IEnumerable<Control>>? SourceActions { get; set; }

    // The selected source's menu: the same as a right-click on its row in the list.
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

    // A menu open over the native surface does not close by itself when clicking elsewhere in
    // the preview: the click never reaches its window. So it is closed here, before opening
    // another one or starting anything else.
    private void CloseSourceMenu()
    {
        var menu = _sourceMenu;
        _sourceMenu = null;
        menu?.Close();
    }

    // Catches a pointer stopping between two writes: without it, the last position would wait
    // for the next move or the release to reach the engine.
    private void OnFlushTick(object? sender, EventArgs e)
    {
        var composition = Composition;
        if (composition is { IsGesturing: true }) composition.FlushGesture();

        // A refusal during catch-up stops the gesture: the overlay returns to what the engine holds,
        // and the bubble has nothing left to say.
        if (composition is not { IsGesturing: true })
        {
            _flushTimer.Stop();
            Readout.IsOpen = false;
        }

        NativePreview.ShowComposition();
    }

    // The picture exactly fills the native surface's rectangle: a screen point maps to it by the
    // ratio of the two sizes alone. This is the engine's coordinate change, reversed, and the
    // only place it is written.
    private (double X, double Y)? ToCanvas(Point point)
    {
        if (!TryGetCanvasScale(out var scaleX, out var scaleY)) return null;

        var picture = PictureBounds;
        return ((point.X - picture.X) * scaleX, (point.Y - picture.Y) * scaleY);
    }

    // The whole canvas: it overflows the visible surface when shown at actual size.
    private Rect PictureBounds => _pictureArea is { } area
        ? new Rect(NativePreview.Bounds.Position + area.Position, area.Size)
        : NativePreview.Bounds;

    // Only what is visible can be targeted: what overflows is cut by the surface.
    private bool IsOnPicture(Point point) =>
        NativePreview.Bounds.Contains(point) && PictureBounds.Contains(point);

    private bool TryGetCanvasScale(out double scaleX, out double scaleY)
    {
        var picture = PictureBounds;
        if (picture.Width <= 0 || picture.Height <= 0 || BaseCanvasWidth <= 0 || BaseCanvasHeight <= 0)
        {
            scaleX = scaleY = 0;
            return false;
        }

        scaleX = BaseCanvasWidth / picture.Width;
        scaleY = BaseCanvasHeight / picture.Height;
        return true;
    }

    // A handle is aimed with the mouse: its grab area is thought in screen pixels, a little
    // wider than the painted square, then converted to canvas coordinates. Same for the
    // rotation area.
    private double HandleTolerance => TryGetCanvasScale(out var scaleX, out _) ? HandleReach * scaleX : 0;

    private double RotationTolerance => TryGetCanvasScale(out var scaleX, out _) ? RotationReach * scaleX : 0;

    private double SnapTolerance => TryGetCanvasScale(out var scaleX, out _) ? SnapReach * scaleX : 0;

    // The mini-map is thought in physical screen pixels, as the engine paints it in its native
    // surface: it lands in the same place for the click and the eye, whatever the Windows
    // scaling.
    private double CanvasPerScreenPixel =>
        TryGetCanvasScale(out var scaleX, out _) ? scaleX / (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1) : 0;

    // The pointer leaves the picture: nothing is hovered any more.
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

    // A handle pulls in the direction of its edge, which turns with the source: the cursor
    // follows that direction, brought to the nearest of the four axes Windows can draw.
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

    // Windows has no rotation cursor: it is drawn once, a light arc arrow on a dark outline,
    // readable on any image.
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

        // The frame sits around the surface, which leaves it room in the area.
        var frame = ShowCanvasFrame ? 2 : 0;
        var availableWidth = Math.Max(1, area.Width - frame);
        var availableHeight = Math.Max(1, area.Height - frame);
        var scale = Math.Min(availableWidth / BaseCanvasWidth, availableHeight / BaseCanvasHeight);
        _pictureArea = null;
        if (IsActualSize)
        {
            // One canvas pixel per screen pixel. What does not fit stays in the native surface,
            // which cuts it: the wheel pans there, starting from the center.
            scale = 1 / (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
            var pictureWidth = BaseCanvasWidth * scale;
            var pictureHeight = BaseCanvasHeight * scale;
            var visibleWidth = Math.Min(availableWidth, pictureWidth);
            var visibleHeight = Math.Min(availableHeight, pictureHeight);
            if (pictureWidth > visibleWidth || pictureHeight > visibleHeight)
            {
                var overflowX = (pictureWidth - visibleWidth) / 2;
                var overflowY = (pictureHeight - visibleHeight) / 2;
                _pan = new Vector(Math.Clamp(_pan.X, -overflowX, overflowX), Math.Clamp(_pan.Y, -overflowY, overflowY));
                _pictureArea = new Rect(_pan.X - overflowX, _pan.Y - overflowY, pictureWidth, pictureHeight);
            }

            NativePreview.Width = Math.Max(1, Math.Round(visibleWidth));
            NativePreview.Height = Math.Max(1, Math.Round(visibleHeight));
        }
        else
        {
            _pan = default;
            NativePreview.Width = Math.Max(1, Math.Round(BaseCanvasWidth * scale));
            NativePreview.Height = Math.Max(1, Math.Round(BaseCanvasHeight * scale));
        }

        NativePreview.SetPictureArea(_pictureArea);
        CanvasFrame.Width = NativePreview.Width + frame;
        CanvasFrame.Height = NativePreview.Height + frame;
    }
}
