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

    public static readonly DirectProperty<ObsPreviewHost, bool> IsPreviewVisibleProperty =
        AvaloniaProperty.RegisterDirect<ObsPreviewHost, bool>(nameof(IsPreviewVisible), host => host.IsPreviewVisible);

    public static readonly DirectProperty<ObsPreviewHost, bool> ShowPlaceholderProperty =
        AvaloniaProperty.RegisterDirect<ObsPreviewHost, bool>(nameof(ShowPlaceholder), host => host.ShowPlaceholder);

    public static readonly DirectProperty<ObsPreviewHost, string> ErrorMessageProperty =
        AvaloniaProperty.RegisterDirect<ObsPreviewHost, string>(nameof(ErrorMessage), host => host.ErrorMessage);

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
        AttachedToVisualTree += (_, _) =>
        {
            _isAttached = true;
            ObserveRuntime(Runtime);
            RefreshPreview();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _isAttached = false;
            ObserveRuntime(null);
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

        _nativeHandle = handle;
        // NativeControlHost can create its child after AttachedToVisualTree. Queue
        // one refresh so a scene selected before HWND creation starts immediately.
        Dispatcher.UIThread.Post(RefreshPreview);
        return new PlatformHandle(handle, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        StopRunningPreview();
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
    }

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
                    await runtime.StopPreviewAsync(scene.Id, CancellationToken.None);
                return;
            }

            if (result.IsSuccess)
            {
                _runningSceneId = scene.Id;
                ErrorMessage = "";
                UpdateVisibility(true);
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
            Runtime.StopPreviewAsync(sceneId.Value, CancellationToken.None).GetAwaiter().GetResult();
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
