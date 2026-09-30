using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using CastorApplication.Services.Studio;
using CastorApplication.ViewModels.Scenes;

namespace CastorApplication.Controls;

public sealed class ObsPreviewHost : NativeControlHost
{
    public static readonly StyledProperty<SceneItemViewModel?> SceneProperty =
        AvaloniaProperty.Register<ObsPreviewHost, SceneItemViewModel?>(nameof(Scene));

    public static readonly StyledProperty<IScenePreviewRuntime?> RuntimeProperty =
        AvaloniaProperty.Register<ObsPreviewHost, IScenePreviewRuntime?>(nameof(Runtime));

    /// <summary>
    /// Set it to have the engine outline every composed source on the picture. Left unset,
    /// the preview shows the picture alone - which is what an on-air view wants.
    /// </summary>
    public static readonly StyledProperty<SceneCompositionViewModel?> CompositionProperty =
        AvaloniaProperty.Register<ObsPreviewHost, SceneCompositionViewModel?>(nameof(Composition));

    public static readonly DirectProperty<ObsPreviewHost, bool> IsPreviewVisibleProperty =
        AvaloniaProperty.RegisterDirect<ObsPreviewHost, bool>(nameof(IsPreviewVisible), host => host.IsPreviewVisible);

    public static readonly DirectProperty<ObsPreviewHost, bool> ShowPlaceholderProperty =
        AvaloniaProperty.RegisterDirect<ObsPreviewHost, bool>(nameof(ShowPlaceholder), host => host.ShowPlaceholder);

    public static readonly DirectProperty<ObsPreviewHost, string> ErrorMessageProperty =
        AvaloniaProperty.RegisterDirect<ObsPreviewHost, string>(nameof(ErrorMessage), host => host.ErrorMessage);

    private readonly DispatcherTimer _compositionTimer;
    private CancellationTokenSource? _refreshCancellation;
    private IScenePreviewRuntime? _observedRuntime;
    private Guid? _runningSceneId;
    private int _refreshVersion;
    private bool _isAttached;
    private IntPtr _nativeHandle;
    private bool _isPreviewVisible;
    private bool _showPlaceholder = true;
    private string _errorMessage = "";

    public SceneItemViewModel? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public IScenePreviewRuntime? Runtime
    {
        get => GetValue(RuntimeProperty);
        set => SetValue(RuntimeProperty, value);
    }

    public SceneCompositionViewModel? Composition
    {
        get => GetValue(CompositionProperty);
        set => SetValue(CompositionProperty, value);
    }

    public bool IsPreviewVisible
    {
        get => _isPreviewVisible;
        private set => SetAndRaise(IsPreviewVisibleProperty, ref _isPreviewVisible, value);
    }

    public bool ShowPlaceholder
    {
        get => _showPlaceholder;
        private set => SetAndRaise(ShowPlaceholderProperty, ref _showPlaceholder, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetAndRaise(ErrorMessageProperty, ref _errorMessage, value);
    }

    public ObsPreviewHost()
    {
        // Nothing tells the interface that a transform moved engine-side, so the outlines
        // are read again on a fixed beat - often enough for the lag not to show, rarely
        // enough not to weigh on the rendering.
        _compositionTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _compositionTimer.Tick += (_, _) => RefreshComposition();

        AttachedToVisualTree += (_, _) =>
        {
            _isAttached = true;
            ObserveRuntime(Runtime);
            UpdateCompositionTimer();
            RefreshPreview();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _isAttached = false;
            ObserveRuntime(null);
            _compositionTimer.Stop();
            StopRunningPreview();
        };
        SizeChanged += (_, _) => ResizeRunningPreview();
        PropertyChanged += OnPropertyChanged;
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("La preview LibObs nécessite Windows.");

        var handle = CreateWindowEx(
            0,
            "STATIC",
            "",
            WindowStyleChild | WindowStyleVisible | WindowStyleClipChildren | WindowStyleClipSiblings |
            StaticStyleBlackRect,
            0,
            0,
            1,
            1,
            parent.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        if (handle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "La surface native de preview n'a pas pu être créée.");

        // Both windows: the click falls through mine onto the host Avalonia puts in
        // between, which would otherwise take it.
        MakeClickThrough(handle, ref _surfaceProc, ref _previousSurfaceProc);
        MakeClickThrough(parent.Handle, ref _hostProc, ref _previousHostProc);

        _hostWindow = parent.Handle;
        _nativeHandle = handle;
        // NativeControlHost can create its child after AttachedToVisualTree. Queue
        // one refresh so a scene selected before HWND creation starts immediately.
        Dispatcher.UIThread.Post(RefreshPreview);
        return new PlatformHandle(handle, "HWND");
    }

    /// <summary>
    /// Makes the surface invisible to the mouse, so clicks land on the Avalonia
    /// content behind it instead of being swallowed.
    /// </summary>
    /// <remarks>
    /// A native child window takes every mouse event over its own rectangle, which left
    /// the video area dead to clicks and drags. Extended styles do not help on a child
    /// window - WS_EX_TRANSPARENT and WS_DISABLED were both tried - so the window
    /// procedure is replaced to answer HTTRANSPARENT to hit tests, which is what tells
    /// Windows to look behind it. The surface itself never takes input: dragging a source
    /// is handled by the Avalonia content behind it (see StudioPreview), which is exactly
    /// where this sends the mouse.
    /// </remarks>
    private static void MakeClickThrough(IntPtr handle, ref WindowProc? proc, ref IntPtr previous)
    {
        if (handle == IntPtr.Zero) return;

        // The delegate is kept alive by the caller's field: Windows holds a raw pointer to
        // it, and a collected delegate would crash the message loop.
        var previousProc = IntPtr.Zero;
        proc = (window, message, wParam, lParam) => message == WindowMessageNcHitTest
            ? new IntPtr(HitTestTransparent)
            : CallWindowProc(previousProc, window, message, wParam, lParam);

        previousProc = SetWindowLongPtr(handle, WindowLongWndProc,
            Marshal.GetFunctionPointerForDelegate(proc));
        previous = previousProc;
    }

    // The host window belongs to Avalonia and can outlive this control, so its original
    // procedure goes back when the surface goes away.
    private void RestoreHostWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero || _previousHostProc == IntPtr.Zero) return;

        SetWindowLongPtr(handle, WindowLongWndProc, _previousHostProc);
        _previousHostProc = IntPtr.Zero;
        _hostProc = null;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        StopRunningPreview();
        RestoreHostWindow(_hostWindow);
        _hostWindow = IntPtr.Zero;
        if (control.Handle != IntPtr.Zero) DestroyWindow(control.Handle);
        _nativeHandle = IntPtr.Zero;
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == SceneProperty)
        {
            RefreshPreview();
        }
        else if (change.Property == RuntimeProperty)
        {
            ObserveRuntime(Runtime);
            RefreshPreview();
        }
        else if (change.Property == BoundsProperty)
        {
            ResizeRunningPreview();
        }
        else if (change.Property == CompositionProperty)
        {
            UpdateCompositionTimer();
            RefreshComposition();
        }
    }

    private void UpdateCompositionTimer()
    {
        if (_isAttached && Composition != null) _compositionTimer.Start();
        else _compositionTimer.Stop();
    }

    // The interface reads the composition, the engine draws it: a native surface cannot be
    // drawn over, so the overlay is handed back to it and painted in the same frame as the
    // picture.
    internal void RefreshComposition()
    {
        if (!CanShowComposition) return;

        Composition!.Refresh();
        ShowComposition();
    }

    // Hands the engine the overlay as it stands, without reading the composition again:
    // what a click or a gesture calls, so the outline follows the pointer at once rather
    // than on the next beat, and without one engine read per mouse move.
    internal void ShowComposition()
    {
        if (!CanShowComposition) return;

        Runtime!.SetCompositionOverlay(_nativeHandle, Composition!.Overlay);
    }

    private bool CanShowComposition =>
        Composition != null && Runtime != null && _runningSceneId != null && _nativeHandle != IntPtr.Zero;

    private void ObserveRuntime(IScenePreviewRuntime? runtime)
    {
        if (_observedRuntime != null)
            _observedRuntime.PreviewResetRequested -= OnPreviewResetRequested;

        _observedRuntime = runtime;
        if (_observedRuntime != null)
            _observedRuntime.PreviewResetRequested += OnPreviewResetRequested;
    }

    private void OnPreviewResetRequested(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(RefreshPreview);

    private async void RefreshPreview()
    {
        var version = ++_refreshVersion;
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        var cancellation = _refreshCancellation = new CancellationTokenSource();

        var runtime = Runtime;
        var scene = Scene;
        var handle = _nativeHandle;
        if (!_isAttached || runtime == null || scene == null || handle == IntPtr.Zero ||
            !runtime.IsAvailable || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            StopRunningPreview();
            ErrorMessage = "";
            UpdateVisibility(false);
            return;
        }

        try
        {
            StopRunningPreview(cancelRefresh: false);
            var result = await runtime.StartPreviewAsync(
                scene.ToDefinition(),
                handle,
                PixelWidth,
                PixelHeight,
                cancellation.Token);

            if (version != _refreshVersion || cancellation.IsCancellationRequested)
            {
                if (result.IsSuccess)
                    await runtime.StopPreviewAsync(handle, scene.Id, CancellationToken.None);
                return;
            }

            if (result.IsSuccess)
            {
                _runningSceneId = scene.Id;
                ErrorMessage = "";
                UpdateVisibility(true);
                // A fresh session starts with no outline: give it this scene's before its
                // first frame, rather than showing a bare picture until the next beat.
                RefreshComposition();
            }
            else
            {
                ErrorMessage = result.Message;
                UpdateVisibility(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (version == _refreshVersion)
            {
                ErrorMessage = exception.Message;
                UpdateVisibility(false);
            }
        }
    }

    private void ResizeRunningPreview()
    {
        if (_runningSceneId == null || Runtime == null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        Runtime.ResizePreview(
            _nativeHandle,
            PixelWidth,
            PixelHeight);
    }

    private uint PixelWidth => (uint)Math.Max(1, Math.Round(Bounds.Width * RenderScaling));
    private uint PixelHeight => (uint)Math.Max(1, Math.Round(Bounds.Height * RenderScaling));
    private double RenderScaling => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;

    private void StopRunningPreview(bool cancelRefresh = true)
    {
        if (cancelRefresh)
        {
            _refreshCancellation?.Cancel();
            _refreshCancellation?.Dispose();
            _refreshCancellation = null;
        }

        var sceneId = _runningSceneId;
        _runningSceneId = null;
        if (sceneId == null || Runtime == null) return;

        try
        {
            Runtime.StopPreviewAsync(_nativeHandle, sceneId.Value, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
        }
    }

    private void UpdateVisibility(bool visible)
    {
        IsPreviewVisible = visible;
        ShowPlaceholder = !visible;
    }

    private const uint WindowStyleChild = 0x40000000;
    private const uint WindowStyleVisible = 0x10000000;
    private const uint WindowStyleClipChildren = 0x02000000;
    private const uint WindowStyleClipSiblings = 0x04000000;
    private const uint StaticStyleBlackRect = 0x00000004;
    private const int WindowLongWndProc = -4;
    private const uint WindowMessageNcHitTest = 0x0084;
    private const int HitTestTransparent = -1;

    private delegate IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private WindowProc? _surfaceProc;
    private IntPtr _previousSurfaceProc;
    private WindowProc? _hostProc;
    private IntPtr _previousHostProc;
    private IntPtr _hostWindow;

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProc(IntPtr previous, IntPtr window, uint message, IntPtr wParam,
        IntPtr lParam);


    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr handle);
}
