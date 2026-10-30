using CastorApplication.Models.Studio;
using CastorApplication.Services.Studio;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CastorApplication.ViewModels.Scenes;

/// <summary>
/// What a canvas point offers to grab: a source to move, or a handle of the selected source
/// to pull.
/// </summary>
public readonly record struct CompositionTarget(CompositionGestureKind Kind, CompositionHandle? Handle);

/// <summary>Where the selected source goes in its scene's stack.</summary>
public enum SourceArrangement
{
    Forward,
    Backward,
    ToFront,
    ToBack
}

/// <summary>
/// A scene's composition as the engine holds it, read continuously: which sources are
/// composed, and which rectangle each takes. None of these values is decided or adjusted here:
/// a value kept locally would end up describing something other than what is really rendered.
/// </summary>
/// <remarks>
/// What is made of it, the frames painted on the preview, is drawn by the engine itself: its
/// surface is a native window, nothing Avalonia draws can sit on it.
/// <para>
/// A gesture (move, stretch, crop) is no exception: it proposes a placement, the engine writes
/// or refuses it, and its answer is what stays. The only thing shown before it is the grabbed
/// source's frame, until it confirms.
/// </para>
/// </remarks>
public partial class SceneCompositionViewModel : ViewModelBase
{
    /// <summary>
    /// Minimum gap between two writes during a gesture: one frame at 60 fps. The pointer moves far
    /// more often than the engine composes; writing every move would show nothing more.
    /// </summary>
    public static readonly TimeSpan WriteInterval = TimeSpan.FromMilliseconds(16);

    private readonly ISourceRuntime _sourceRuntime;
    private readonly TimeProvider _time;
    // Stacking does not belong to the composition: the source list holds it and reads it back
    // from the engine. The canvas only asks it to.
    private readonly Action<Guid, SourceArrangement>? _arrange;
    // What the toolbar buttons last received: they only hear of a real change, not of every
    // pointer move.
    private bool _notifiedSelection;
    private bool _notifiedCropMode;
    private double _notifiedZoomFactor = SourceZoom.None.Factor;
    private SceneItemViewModel? _scene;
    private Guid? _selectedSourceId;
    private Gesture? _gesture;
    // The held crop mode (double-click, menu) and the one Alt gives while held. Both only apply
    // to the selected source.
    private bool _cropMode;
    private bool _cropModifier;
    private Guid? _hoveredSourceId;
    // The canvas size at the last read: its edges and center snap moving sources.
    private int _canvasWidth;
    private int _canvasHeight;

    // A gesture in progress: what it grabs, where it starts, what it requests, and what the
    // engine has already written of it.
    private sealed class Gesture
    {
        public required Guid SceneId { get; init; }
        public required CompositionGestureKind Kind { get; init; }
        public required CompositionHandle? Handle { get; init; }
        public required SourceTransform Start { get; init; }
        public required double OriginX { get; init; }
        public required double OriginY { get; init; }
        public SourcePlacement Requested { get; set; }
        // What a zoom slide requests: the frame does not move.
        public SourceZoom RequestedZoom { get; set; }
        // The grabbed mini-map, in frame coordinates, for a gesture aiming on it.
        public FrameRect Map { get; init; }
        public bool HasPendingWrite { get; set; }
        public bool HasWritten { get; set; }
        public long? LastWrite { get; set; }
        public IReadOnlyList<CompositionGuide> Guides { get; set; } = [];
    }

    /// <summary>
    /// What the engine must draw over its picture: the sources it composes, in drawing order (from
    /// background to foreground), and the selected one. Each carries the color the source list
    /// names it with.
    /// </summary>
    public CompositionOverlay Overlay { get; private set; } = CompositionOverlay.Empty;

    /// <summary>The selected source, as the engine composes it right now.</summary>
    public SourceTransform? Selected => Overlay.Selected?.Transform;

    /// <summary>Whether a gesture is in progress on a source.</summary>
    public bool IsGesturing => _gesture != null;

    /// <summary>Whether a source is selected: menu and handles apply to it.</summary>
    public bool HasSelection => Overlay.Selected != null;

    /// <summary>Whether crop mode is held on the selected source, Alt aside.</summary>
    public bool IsCropMode => _cropMode && HasSelection;

    /// <summary>
    /// Whether the selected source shows and grabs as cropping: held mode, or Alt held.
    /// </summary>
    public bool IsCropping => Overlay.IsCropping;

    [ObservableProperty] private string _status = "";

    internal SceneCompositionViewModel(
        ISourceRuntime sourceRuntime,
        TimeProvider? time = null,
        Action<Guid, SourceArrangement>? arrange = null)
    {
        _sourceRuntime = sourceRuntime;
        _time = time ?? TimeProvider.System;
        _arrange = arrange;
    }

    /// <summary>Follows this scene from now on, starting from scratch.</summary>
    internal void ShowScene(SceneItemViewModel? scene)
    {
        // A gesture belongs to the scene it started in: what it requested is written before leaving
        // the scene, rather than lost on the way.
        FinishGesture(reconcile: false);
        _scene = scene;
        // Changing scene means changing sources: keeping the previous selection would reopen the next
        // scene with a selection that is not its own.
        _selectedSourceId = null;
        _cropMode = false;
        Publish([]);
        Refresh();
    }

    /// <summary>
    /// Selects the source targeted at this canvas point, or none if the point falls on none. The
    /// choice goes to the one in front: that is the one the operator sees at that spot.
    /// </summary>
    public void SelectAt(double canvasX, double canvasY)
    {
        var source = SourceAt(canvasX, canvasY);
        if (source == null)
        {
            ClearSelection();
            return;
        }

        // Cropping applies to one source: selecting another ends it.
        if (source.SourceId != _selectedSourceId) _cropMode = false;
        _selectedSourceId = source.SourceId;
        Publish(Overlay.Sources);
    }

    /// <summary>
    /// The pointer hovers this canvas point: the source in front shows at the selection weight, to
    /// say before the click which one it will take.
    /// </summary>
    /// <returns>Whether the hovered source changed, so whether to hand the overlay to the engine.</returns>
    public bool HoverAt(double canvasX, double canvasY) => SetHovered(SourceAt(canvasX, canvasY)?.SourceId);

    /// <summary>The pointer left the picture: no source is hovered any more.</summary>
    /// <returns>Whether a source was hovered.</returns>
    public bool ClearHover() => SetHovered(null);

    private bool SetHovered(Guid? sourceId)
    {
        if (_hoveredSourceId == sourceId) return false;

        _hoveredSourceId = sourceId;
        Publish(Overlay.Sources);
        return true;
    }

    /// <summary>
    /// Moves to the next source in the stack, in source list order: towards the background for a
    /// positive <paramref name="direction"/>, towards the foreground otherwise. With no selected
    /// source, it starts from the foreground (or from the background when going backwards).
    /// </summary>
    /// <returns>
    /// Whether a source ends up selected. Past the last one, nothing is: that is what lets Tab
    /// leave the preview instead of going round in circles.
    /// </returns>
    public bool SelectNext(int direction)
    {
        if (_gesture != null || direction == 0) return HasSelection;

        // The list runs from background to foreground: moving forward in the stack means walking
        // it backwards.
        var sources = Overlay.Sources;
        var current = -1;
        for (var index = 0; index < sources.Count; index++)
        {
            if (sources[index].Transform.SourceId == _selectedSourceId) current = index;
        }

        var next = current < 0
            ? direction > 0 ? sources.Count - 1 : 0
            : current - Math.Sign(direction);
        if (next < 0 || next >= sources.Count)
        {
            ClearSelection();
            return false;
        }

        var sourceId = sources[next].Transform.SourceId;
        if (sourceId != _selectedSourceId) _cropMode = false;
        _selectedSourceId = sourceId;
        Publish(sources);
        return true;
    }

    /// <summary>
    /// Selects this source if the engine composes it: that is what a right-click on its list row
    /// does. A source it does not compose (hidden, audio) has no frame to select; the previous
    /// selection is then dropped, so as not to target another source.
    /// </summary>
    /// <returns>Whether the source is selected on the canvas.</returns>
    public bool Select(Guid sourceId)
    {
        if (_gesture != null) return Selected?.SourceId == sourceId;

        if (Find(Overlay.Sources, sourceId) == null)
        {
            ClearSelection();
            return false;
        }

        if (sourceId != _selectedSourceId) _cropMode = false;
        _selectedSourceId = sourceId;
        Publish(Overlay.Sources);
        return true;
    }

    /// <summary>
    /// What a key, a toolbar button or a menu row asks for: they all go through here, which
    /// guarantees them the same effect.
    /// </summary>
    /// <returns>
    /// Whether the command was used. Otherwise the key carries on: Tab past the last source gives
    /// focus back to the page, an arrow with no selected source navigates.
    /// </returns>
    public bool Execute(CompositionKeyAction action)
    {
        // A command in the middle of a gesture would overwrite what the pointer is asking for.
        if (_gesture != null) return false;

        switch (action.Command)
        {
            case CompositionCommand.SelectNext or CompositionCommand.SelectPrevious:
                return SelectNext(action.Command == CompositionCommand.SelectNext ? 1 : -1);
            case var _ when !HasSelection:
                return false;
            // While cropping, arrows slide the zoomed image under its frame, like Alt + drag with
            // the mouse; otherwise they push the frame.
            case CompositionCommand.Nudge when IsCropping && Selected is { Zoom.IsZoomed: true }:
                NudgeSelectedZoom(Math.Sign(action.StepX), Math.Sign(action.StepY));
                break;
            case CompositionCommand.Nudge:
                NudgeSelected(action.StepX, action.StepY);
                break;
            case CompositionCommand.CenterSelected:
                CenterSelected();
                break;
            case CompositionCommand.FitSelected:
                FitSelected();
                break;
            case CompositionCommand.ResetTransform:
                ResetSelectedTransform();
                break;
            case CompositionCommand.RotateRight:
                RotateSelected(90);
                break;
            case CompositionCommand.RotateLeft:
                RotateSelected(-90);
                break;
            case CompositionCommand.ToggleCrop:
                ToggleCropMode();
                break;
            case CompositionCommand.ZoomIn:
                StepSelectedZoom(1);
                break;
            case CompositionCommand.ZoomOut:
                StepSelectedZoom(-1);
                break;
            case CompositionCommand.ResetZoom:
                ResetSelectedZoom();
                break;
            case CompositionCommand.BringForward:
                ArrangeSelected(SourceArrangement.Forward);
                break;
            case CompositionCommand.SendBackward:
                ArrangeSelected(SourceArrangement.Backward);
                break;
            case CompositionCommand.BringToFront:
                ArrangeSelected(SourceArrangement.ToFront);
                break;
            case CompositionCommand.SendToBack:
                ArrangeSelected(SourceArrangement.ToBack);
                break;
            default:
                return false;
        }

        return true;
    }

    /// <summary>Selects no source any more: the overlay only shows the frames.</summary>
    public void ClearSelection()
    {
        if (_selectedSourceId == null) return;

        _selectedSourceId = null;
        _cropMode = false;
        Publish(Overlay.Sources);
    }

    /// <summary>
    /// What a gesture started at this point would grab: a handle of the selected source, within
    /// <paramref name="handleTolerance"/>; otherwise, outside the source but within
    /// <paramref name="rotationReach"/> of one of its corners, its rotation; otherwise the source
    /// in front. Handles come first: they are painted over the other frames. The mini-map of a
    /// selected, zoomed source comes before them: it is painted in the frame, over the image.
    /// </summary>
    /// <param name="crop">Whether Alt is held: handles crop instead of stretching.</param>
    /// <param name="canvasPerScreenPixel">
    /// Canvas pixels per screen pixel, which locates the mini-map; 0 without a mini-map.
    /// </param>
    public CompositionTarget? TargetAt(
        double canvasX,
        double canvasY,
        double handleTolerance,
        bool crop,
        double rotationReach = 0,
        double canvasPerScreenPixel = 0)
    {
        var selected = Selected;
        if (selected != null && MapAt(selected, canvasX, canvasY, canvasPerScreenPixel) != null)
            return new CompositionTarget(CompositionGestureKind.Aim, null);

        if (selected != null)
        {
            var cropping = crop || _cropMode;
            var handle = CompositionGeometry.HandleAt(selected, canvasX, canvasY, handleTolerance);
            if (handle != null)
            {
                return new CompositionTarget(
                    cropping ? CompositionGestureKind.Crop : CompositionGestureKind.Resize, handle);
            }

            // While cropping, the inside of a zoomed source slides its image under the frame: without
            // Alt you act on the frame; with it, on what the frame shows.
            if (cropping && selected.Zoom.IsZoomed && CompositionGeometry.Contains(selected, canvasX, canvasY))
                return new CompositionTarget(CompositionGestureKind.Pan, null);

            // While cropping, the source does not turn: only the brackets can be grabbed.
            var corner = cropping || rotationReach <= 0
                ? null
                : CompositionGeometry.RotationCornerAt(selected, canvasX, canvasY, rotationReach);
            if (corner != null) return new CompositionTarget(CompositionGestureKind.Rotate, corner);
        }

        return SourceAt(canvasX, canvasY) != null
            ? new CompositionTarget(CompositionGestureKind.Move, null)
            : null;
    }

    /// <summary>
    /// Starts a gesture at this canvas point. On a handle of the selected source, it stretches it,
    /// or crops it in crop mode or when <paramref name="crop"/> is requested. Just outside one of
    /// its corners, it rotates it. On a source, it selects and moves it. Beside any source, it
    /// selects nothing any more.
    /// </summary>
    /// <returns>What the gesture grabbed, or nothing.</returns>
    public CompositionTarget? BeginGesture(
        double canvasX,
        double canvasY,
        double handleTolerance,
        bool crop,
        double rotationReach = 0,
        double canvasPerScreenPixel = 0)
    {
        FinishGesture(reconcile: false);

        var scene = _scene;
        var target = TargetAt(canvasX, canvasY, handleTolerance, crop, rotationReach, canvasPerScreenPixel);
        if (scene == null || target == null)
        {
            ClearSelection();
            return null;
        }

        var start = target.Value.Kind == CompositionGestureKind.Move
            ? SourceAt(canvasX, canvasY)!
            : Selected!;

        // Grabbing another source means leaving the previous one's crop.
        if (start.SourceId != _selectedSourceId) _cropMode = false;
        _selectedSourceId = start.SourceId;
        _gesture = new Gesture
        {
            SceneId = scene.Id,
            Kind = target.Value.Kind,
            Handle = target.Value.Handle,
            Start = start,
            OriginX = canvasX,
            OriginY = canvasY,
            Requested = start.Placement,
            RequestedZoom = start.Zoom,
            Map = MapAt(start, canvasX, canvasY, canvasPerScreenPixel) ?? default
        };
        Publish(Overlay.Sources);

        // A click on the mini-map jumps there at once, without waiting for the pointer to move.
        if (target.Value.Kind == CompositionGestureKind.Aim)
            UpdateGesture(canvasX, canvasY, keepAspectRatio: true);

        return target;
    }

    // This source's mini-map, if it shows one and this canvas point falls on it.
    private static FrameRect? MapAt(SourceTransform source, double canvasX, double canvasY, double canvasPerScreenPixel)
    {
        if (!source.Zoom.IsZoomed) return null;
        if (ZoomMap.Bounds(source.Width, source.Height, canvasPerScreenPixel) is not { } map) return null;

        var (u, v) = CompositionGeometry.ToLocal(source, canvasX, canvasY);
        return map.Contains(u, v) ? map : null;
    }

    /// <summary>
    /// The pointer is now at this canvas point. The frame follows at once; the engine gets at most
    /// one write per <see cref="WriteInterval"/>, the last request waiting for
    /// <see cref="FlushGesture"/> or the end of the gesture.
    /// </summary>
    /// <param name="keepAspectRatio">Whether a corner keeps the source's proportions.</param>
    /// <param name="constrainRotation">
    /// Whether a rotation advances by steps of <see cref="CompositionGeometry.RotationStep"/>°.
    /// </param>
    /// <param name="snapReach">
    /// Distance, in canvas pixels, at which a moved source snaps to the edges and centers of the
    /// canvas and other sources; 0 for no snapping.
    /// </param>
    public void UpdateGesture(
        double canvasX,
        double canvasY,
        bool keepAspectRatio,
        bool constrainRotation = false,
        double snapReach = 0)
    {
        var gesture = _gesture;
        if (gesture == null) return;

        var deltaX = canvasX - gesture.OriginX;
        var deltaY = canvasY - gesture.OriginY;

        if (IsZoomGesture(gesture.Kind))
        {
            var zoom = gesture.Kind == CompositionGestureKind.Aim
                ? AimedZoom(gesture, canvasX, canvasY)
                : CompositionGeometry.Pan(gesture.Start, deltaX, deltaY);
            if (zoom == gesture.RequestedZoom) return;

            gesture.RequestedZoom = zoom;
            Request(gesture);
            return;
        }

        var requested = gesture.Kind switch
        {
            CompositionGestureKind.Resize => CompositionGeometry.Resize(
                gesture.Start, gesture.Handle!.Value, deltaX, deltaY, keepAspectRatio),
            CompositionGestureKind.Crop => CompositionGeometry.Crop(
                gesture.Start, gesture.Handle!.Value, deltaX, deltaY),
            CompositionGestureKind.Rotate => CompositionGeometry.Rotate(
                gesture.Start, gesture.OriginX, gesture.OriginY, canvasX, canvasY, constrainRotation),
            _ => CompositionGeometry.Move(gesture.Start, deltaX, deltaY)
        };

        if (gesture.Kind == CompositionGestureKind.Move)
        {
            (requested, gesture.Guides) = CompositionGeometry.Snap(
                gesture.Start,
                requested,
                Overlay.Sources.Select(source => source.Transform),
                _canvasWidth,
                _canvasHeight,
                snapReach);
        }

        if (requested == gesture.Requested) return;

        gesture.Requested = requested;
        Request(gesture);
    }

    // The frame follows at once; the engine, at its own pace.
    private void Request(Gesture gesture)
    {
        gesture.HasPendingWrite = true;
        Publish(Overlay.Sources);

        if (IsDue(gesture))
            Write(gesture);
    }

    /// <summary>
    /// Writes the gesture's last request if it is still waiting and the interval has passed: that
    /// catches a pointer stopping between two writes.
    /// </summary>
    public void FlushGesture()
    {
        var gesture = _gesture;
        if (gesture is not { HasPendingWrite: true }) return;
        if (!IsDue(gesture)) return;

        Write(gesture);
    }

    /// <summary>
    /// Ends the gesture: its last request is written, then everything is read back from the
    /// engine. What stays on screen is what it confirmed, not what was requested.
    /// </summary>
    public void EndGesture() => FinishGesture(reconcile: true);

    /// <summary>
    /// Abandons the gesture: the source returns in the engine to the placement it had when the
    /// gesture started.
    /// </summary>
    public void CancelGesture()
    {
        var gesture = _gesture;
        if (gesture == null) return;

        _gesture = null;
        var restored = Restore(gesture);
        Refresh();
        if (!restored.IsSuccess) Status = restored.Message;
    }

    /// <summary>
    /// What the current gesture writes, in plain text, for the bubble following the pointer: the
    /// position, size, crop or angle. Empty outside a gesture.
    /// </summary>
    public string GestureReadout
    {
        get
        {
            var gesture = _gesture;
            if (gesture == null) return "";

            var shown = Shown(gesture);
            var crop = shown.Crop;
            return gesture.Kind switch
            {
                CompositionGestureKind.Pan or CompositionGestureKind.Aim =>
                    $"Centre {shown.Zoom.CenterX * 100:0} % · {shown.Zoom.CenterY * 100:0} %",
                CompositionGestureKind.Resize => $"{shown.Width:0} × {shown.Height:0}",
                CompositionGestureKind.Crop =>
                    $"Gauche {crop.Left} · Haut {crop.Top} · Droite {crop.Right} · Bas {crop.Bottom}",
                CompositionGestureKind.Rotate => $"{shown.Rotation:0.#}°",
                _ => $"X {shown.X:0} · Y {shown.Y:0}"
            };
        }
    }

    /// <summary>
    /// Holds crop mode on the selected source: its handles crop until leaving it. With no selected
    /// source, there is nothing to crop.
    /// </summary>
    public void EnterCropMode()
    {
        if (!HasSelection || _cropMode) return;

        _cropMode = true;
        Publish(Overlay.Sources);
    }

    /// <summary>Leaves crop mode: handles stretch again.</summary>
    public void ExitCropMode()
    {
        if (!_cropMode) return;

        _cropMode = false;
        Publish(Overlay.Sources);
    }

    /// <summary>Enters crop mode, or leaves it if already held.</summary>
    public void ToggleCropMode()
    {
        if (_cropMode) ExitCropMode();
        else EnterCropMode();
    }

    /// <summary>
    /// A wheel step while cropping (Alt held or mode held): the source under the pointer is
    /// selected and its image zooms by <paramref name="steps"/> steps, the point under the pointer
    /// staying where it is. Outside cropping, the wheel touches nothing: a stray wheel turn must
    /// not change the image on air.
    /// </summary>
    /// <returns>Whether the wheel was used.</returns>
    public bool ZoomAt(double canvasX, double canvasY, double steps)
    {
        if (!(_cropMode || _cropModifier) || _gesture != null || steps == 0) return false;

        if (Selected is not { } selected || !CompositionGeometry.Contains(selected, canvasX, canvasY))
        {
            SelectAt(canvasX, canvasY);
            if (Selected is not { } aimed) return false;
            selected = aimed;
        }

        var factor = Stepped(selected.Zoom.Factor, steps);
        if (factor == selected.Zoom.Factor) return true;

        ApplyZoom(selected, CompositionGeometry.ZoomAt(selected, canvasX, canvasY, factor));
        return true;
    }

    // A wheel or key step enlarges by 15%: from ×1 to ×2 in five steps.
    private const double ZoomStep = 1.15;

    private static double Stepped(double factor, double steps) =>
        Math.Clamp(factor * Math.Pow(ZoomStep, steps), SourceZoom.MinFactor, SourceZoom.MaxFactor);

    // An arrow slides the image by a tenth of what the frame shows of it.
    private const double NudgeShare = 0.1;

    /// <summary>
    /// The selected source's image zooms by <paramref name="steps"/> steps around the point it
    /// already aims at: this is keyboard zoom, which has no pointer.
    /// </summary>
    public void StepSelectedZoom(int steps)
    {
        if (Selected is not { } selected) return;

        var factor = Stepped(selected.Zoom.Factor, steps);
        if (factor != selected.Zoom.Factor) ZoomSelected(factor);
    }

    /// <summary>
    /// The selected source's zoomed image slides under its frame, a tenth of the frame per step,
    /// in the arrow's direction: <paramref name="columns"/> to the right, <paramref name="rows"/>
    /// down. Without zoom, there is nothing to slide.
    /// </summary>
    public void NudgeSelectedZoom(int columns, int rows)
    {
        if (Selected is not { Zoom.IsZoomed: true } selected) return;

        var zoom = selected.Zoom;
        var shown = zoom.Aimed(zoom.CenterX, zoom.CenterY);
        var step = NudgeShare / zoom.Factor;
        var nudged = shown.Aimed(shown.CenterX + columns * step, shown.CenterY + rows * step);
        if (nudged != zoom) ApplyZoom(selected, nudged);
    }

    /// <summary>
    /// Alt pressed or released: while held, the selected source shows and grabs as cropping,
    /// without the mode staying afterwards.
    /// </summary>
    public void SetCropModifier(bool held)
    {
        if (_cropModifier == held) return;

        _cropModifier = held;
        Publish(Overlay.Sources);
    }

    /// <summary>The selected source turns by <paramref name="degrees"/> around its center.</summary>
    public void RotateSelected(double degrees)
    {
        if (Selected is { } selected) Apply(selected, CompositionGeometry.RotateBy(selected, degrees));
    }

    /// <summary>
    /// The selected source shifts by <paramref name="deltaX"/>, <paramref name="deltaY"/> canvas
    /// pixels. No snapping: a source is pushed pixel by pixel precisely to put it where snapping
    /// would not.
    /// </summary>
    public void NudgeSelected(double deltaX, double deltaY)
    {
        if (Selected is { } selected) Apply(selected, CompositionGeometry.Move(selected, deltaX, deltaY));
    }

    /// <summary>The selected source goes to the center of the canvas.</summary>
    public void CenterSelected()
    {
        if (Selected is { } selected && HasCanvas)
            Apply(selected, CompositionGeometry.CenterIn(selected, _canvasWidth, _canvasHeight));
    }

    /// <summary>
    /// The selected source takes the largest size at which it fits whole in the canvas, centered.
    /// </summary>
    public void FitSelected()
    {
        if (Selected is { } selected && HasCanvas)
            Apply(selected, CompositionGeometry.Fit(selected, _canvasWidth, _canvasHeight));
    }

    /// <summary>
    /// The selected source goes back to its original placement: top-left corner, source size, no
    /// rotation, crop or zoom.
    /// </summary>
    public void ResetSelectedTransform()
    {
        if (Selected is not { } selected) return;

        Apply(selected, CompositionGeometry.Reset());
        // A refused placement stops here: the zoom is not sent for a source the engine has not
        // put back.
        if (Status.Length == 0 && Selected is { Zoom.IsZoomed: true } reset) ApplyZoom(reset, SourceZoom.None);
    }

    /// <summary>
    /// The selected source changes layer. The source list writes it and reads the stack back:
    /// the composition follows right after.
    /// </summary>
    public void ArrangeSelected(SourceArrangement arrangement)
    {
        if (_gesture == null && Selected is { } selected) _arrange?.Invoke(selected.SourceId, arrangement);
    }

    private bool HasCanvas => _canvasWidth > 0 && _canvasHeight > 0;

    /// <summary>The selected source straightens up, around its center.</summary>
    public void ResetSelectedRotation()
    {
        if (Selected is { } selected) Apply(selected, CompositionGeometry.RotateBy(selected, -selected.Rotation));
    }

    /// <summary>The selected source returns to its full size, what was visible staying in place.</summary>
    public void ResetSelectedCrop()
    {
        if (Selected is { } selected) Apply(selected, CompositionGeometry.ResetCrop(selected));
    }

    /// <summary>
    /// The selected source's image enlarges by <paramref name="factor"/> in its frame, around the
    /// point its zoom already aims at, or the image center if there is none.
    /// </summary>
    public void ZoomSelected(double factor)
    {
        if (Selected is { } selected) ApplyZoom(selected, selected.Zoom with { Factor = factor });
    }

    /// <summary>
    /// The selected source's image zoom as the engine holds it, ×1 with no source. This is the
    /// toolbar slider: writing it asks the engine for that zoom, and the value read afterwards
    /// is the one it confirmed. Zoom given elsewhere (wheel, keyboard, menu) makes it follow.
    /// </summary>
    public double SelectedZoomFactor
    {
        get => Selected?.Zoom.Factor ?? SourceZoom.None.Factor;
        set
        {
            // The slider sends back the value it was just given: nothing to write then.
            if (Selected is { } selected && Math.Abs(value - selected.Zoom.Factor) > 0.001) ZoomSelected(value);
        }
    }

    /// <summary>The selected source shows its full image again.</summary>
    public void ResetSelectedZoom()
    {
        if (Selected is { } selected) ApplyZoom(selected, SourceZoom.None);
    }

    // A one-shot command (menu): one write, then a read from the engine. A refusal leaves what
    // the engine holds and is reported, as for a gesture.
    private void Apply(SourceTransform selected, SourcePlacement placement) =>
        Command(() => _sourceRuntime.SetSourceTransform(_scene!.Id, selected.SourceId, placement));

    private void ApplyZoom(SourceTransform selected, SourceZoom zoom) =>
        Command(() => _sourceRuntime.SetSourceZoom(_scene!.Id, selected.SourceId, zoom));

    private void Command(Func<SourceTransformResult> write)
    {
        if (_scene == null || _gesture != null) return;

        var result = write();
        Refresh();
        if (!result.IsSuccess) Status = result.Message;
    }

    /// <summary>
    /// Reads the composition from the engine. Called after each gesture and at regular intervals
    /// by the preview: that makes frames follow when a transform changes on the engine side,
    /// without the interface being told.
    /// </summary>
    public void Refresh()
    {
        var scene = _scene;
        if (scene == null)
        {
            Publish([]);
            Status = "";
            return;
        }

        var result = _sourceRuntime.GetSceneComposition(scene.Id);
        if (result.Status == StudioRuntimeStatus.Unavailable)
        {
            // A globally unavailable engine is already announced by the preview: here, there is simply
            // nothing composed.
            Publish([]);
            Status = "";
            return;
        }

        if (!result.IsSuccess)
        {
            // Read refused: the last frames stay in place rather than vanish on a passing incident, but
            // the refusal is reported.
            Status = result.Message;
            return;
        }

        Status = "";
        _canvasWidth = result.Composition.CanvasWidth;
        _canvasHeight = result.Composition.CanvasHeight;
        Publish(Drawable(scene, result.Composition.Sources));
    }

    private void FinishGesture(bool reconcile)
    {
        var gesture = _gesture;
        if (gesture == null) return;

        if (gesture.HasPendingWrite && !Write(gesture)) return;

        _gesture = null;
        if (reconcile) Refresh();
    }

    // A refused write stops the gesture there: the source returns in the engine to its
    // pre-gesture placement, and the screen rereads what the engine holds. Nothing remains of the
    // refused request, on screen or in the engine.
    private bool Write(Gesture gesture)
    {
        var result = IsZoomGesture(gesture.Kind)
            ? _sourceRuntime.SetSourceZoom(gesture.SceneId, gesture.Start.SourceId, gesture.RequestedZoom)
            : _sourceRuntime.SetSourceTransform(gesture.SceneId, gesture.Start.SourceId, gesture.Requested);
        gesture.LastWrite = _time.GetTimestamp();
        gesture.HasPendingWrite = false;
        if (result.IsSuccess)
        {
            gesture.HasWritten = true;
            return true;
        }

        _gesture = null;
        Restore(gesture);
        Refresh();
        Status = result.Message;
        return false;
    }

    // A gesture's first request goes out at once: that is what makes the source feel grabbed.
    private bool IsDue(Gesture gesture) =>
        gesture.LastWrite is not { } lastWrite || _time.GetElapsedTime(lastWrite) >= WriteInterval;

    private SourceTransformResult Restore(Gesture gesture) =>
        !gesture.HasWritten
            ? SourceTransformResult.Success(gesture.Start)
            : IsZoomGesture(gesture.Kind)
                ? _sourceRuntime.SetSourceZoom(gesture.SceneId, gesture.Start.SourceId, gesture.Start.Zoom)
                : _sourceRuntime.SetSourceTransform(gesture.SceneId, gesture.Start.SourceId, gesture.Start.Placement);

    // These gestures change what the frame shows, not the frame.
    private static bool IsZoomGesture(CompositionGestureKind kind) =>
        kind is CompositionGestureKind.Pan or CompositionGestureKind.Aim;

    private static SourceZoom AimedZoom(Gesture gesture, double canvasX, double canvasY)
    {
        var (u, v) = CompositionGeometry.ToLocal(gesture.Start, canvasX, canvasY);
        return ZoomMap.Aim(gesture.Start.Zoom, gesture.Map, u, v);
    }

    // What the gesture requests, shown before the engine confirms.
    private static SourceTransform Shown(Gesture gesture) =>
        IsZoomGesture(gesture.Kind)
            ? gesture.Start with { Zoom = gesture.RequestedZoom }
            : CompositionGeometry.Preview(gesture.Start, gesture.Requested);

    // The source in front at this canvas point. The list runs from background to foreground: it
    // is walked backwards to meet what is on top first.
    private SourceTransform? SourceAt(double canvasX, double canvasY)
    {
        var sources = Overlay.Sources;
        for (var index = sources.Count - 1; index >= 0; index--)
        {
            var source = sources[index].Transform;
            if (CompositionGeometry.Contains(source, canvasX, canvasY)) return source;
        }

        return null;
    }

    // Selection is kept by id, never by rectangle: that makes it follow the source when the
    // engine moves it, and drop by itself when the source stops being composed (removed, hidden,
    // or scene changed).
    private void Publish(IReadOnlyList<OverlaySource> sources)
    {
        sources = WithGesture(sources);
        var selected = Find(sources, _selectedSourceId);
        if (selected == null)
        {
            _selectedSourceId = null;
            _cropMode = false;
        }

        // Hover stays quiet during a gesture, and on the selected source: it already has that weight.
        var hovered = _gesture == null && _hoveredSourceId != _selectedSourceId
            ? Find(sources, _hoveredSourceId)
            : null;

        Overlay = new CompositionOverlay(
            sources,
            selected,
            selected != null && (_cropMode || _cropModifier),
            hovered,
            _gesture?.Guides ?? []);

        if (_notifiedSelection != HasSelection)
        {
            _notifiedSelection = HasSelection;
            OnPropertyChanged(nameof(HasSelection));
        }

        if (_notifiedCropMode != IsCropMode)
        {
            _notifiedCropMode = IsCropMode;
            OnPropertyChanged(nameof(IsCropMode));
        }

        if (_notifiedZoomFactor != SelectedZoomFactor)
        {
            _notifiedZoomFactor = SelectedZoomFactor;
            OnPropertyChanged(nameof(SelectedZoomFactor));
        }

        OverlayChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The overlay changed: the preview hands it to the engine without waiting for its periodic
    /// read. This is what makes frames follow a selection or command from somewhere other than
    /// the picture, such as the source list.
    /// </summary>
    public event EventHandler? OverlayChanged;

    // During a gesture, the grabbed source is shown where the gesture asks for it, without
    // waiting for the next write or engine read: that keeps the frame stuck to the pointer. Other
    // sources stay what the engine says.
    private IReadOnlyList<OverlaySource> WithGesture(IReadOnlyList<OverlaySource> sources)
    {
        var gesture = _gesture;
        if (gesture == null) return sources;

        var shown = Shown(gesture);
        var result = new List<OverlaySource>(sources.Count);
        foreach (var source in sources)
        {
            result.Add(source.Transform.SourceId == gesture.Start.SourceId
                ? source with { Transform = shown }
                : source);
        }

        return result;
    }

    private static OverlaySource? Find(IReadOnlyList<OverlaySource> sources, Guid? sourceId)
    {
        if (sourceId == null) return null;

        foreach (var source in sources)
        {
            if (source.Transform.SourceId == sourceId) return source;
        }

        return null;
    }

    // The engine returns its stack from foreground to background; it is walked backwards because
    // painting goes from back to front. Each source leaves with the color the list gives it: on an
    // overlapping composition, that tells which frame is which row. A new list on every read,
    // since the engine's graphics thread rereads the one it was given every frame.
    private static IReadOnlyList<OverlaySource> Drawable(
        SceneItemViewModel scene,
        IReadOnlyList<SourceTransform> transforms)
    {
        var drawable = new List<OverlaySource>(transforms.Count);
        for (var index = transforms.Count - 1; index >= 0; index--)
        {
            var transform = transforms[index];
            // A source the engine does not compose (hidden, or with no image like an audio source) has
            // no frame to show.
            if (!transform.IsVisible || transform.Width <= 0 || transform.Height <= 0) continue;

            drawable.Add(new OverlaySource(transform, TintOf(scene, transform.SourceId)));
        }

        return drawable;
    }

    private static OverlayTint TintOf(SceneItemViewModel scene, Guid sourceId)
    {
        foreach (var source in scene.Sources)
        {
            if (source.Id == sourceId) return OverlayTint.Parse(source.Color);
        }

        return OverlayTint.Default;
    }
}
