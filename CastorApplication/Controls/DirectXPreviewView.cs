using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Castor.Engine.Models;
using Castor.Native;

namespace CastorApplication.Controls;

public sealed class DirectXPreviewView : NativeControlHost
{
    public static readonly StyledProperty<SceneItem?> SceneProperty =
        AvaloniaProperty.Register<DirectXPreviewView, SceneItem?>(nameof(Scene));

    public SceneItem? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    private IntPtr _preview = IntPtr.Zero;
    private IntPtr _hwnd = IntPtr.Zero;
    private IntPtr _previousSourceKey = IntPtr.Zero;
    private bool _isStarted;

    static DirectXPreviewView()
    {
        SceneProperty.Changed.AddClassHandler<DirectXPreviewView>((view, _) => view.UpdatePreviewSource());
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
            ResizePreview();
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DirectX preview is only supported on Windows.");

        var parentHwnd = parent.Handle;
        NativeMethods.EnsureWindowClass();
        _hwnd = NativeMethods.CreatePreviewWindow(parentHwnd);
        _preview = CastorNative.PreviewCreate();

        if (_preview != IntPtr.Zero && _hwnd != IntPtr.Zero)
        {
            var attachResult = CastorNative.PreviewAttachHwnd(_preview, _hwnd);
            if (attachResult != 0)
                Debug.WriteLine($"[DirectXPreview] Attach failed: {attachResult}");
        }

        ResizePreview();
        UpdatePreviewSource();
        return new PlatformHandle(_hwnd, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        StopPreview();

        if (_preview != IntPtr.Zero)
        {
            CastorNative.PreviewDestroy(_preview);
            _preview = IntPtr.Zero;
        }

        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        base.DestroyNativeControlCore(control);
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        StopPreview();
        base.OnUnloaded(e);
    }

    private void UpdatePreviewSource()
    {
        if (_preview == IntPtr.Zero)
            return;

        var source = GetVideoSourceInfo(Scene);
        if (source == null)
        {
            StopPreview();
            return;
        }

        var key = GetSourceKey(source.Value);
        if (_isStarted && key == _previousSourceKey)
            return;

        var result = _isStarted
            ? CastorNative.PreviewSwitchSource(_preview, source.Value)
            : CastorNative.PreviewStart(_preview, source.Value, 30);

        if (result == 0)
        {
            _isStarted = true;
            _previousSourceKey = key;
        }
        else
        {
            Debug.WriteLine($"[DirectXPreview] Start/switch failed: {result}");
            _isStarted = false;
            _previousSourceKey = IntPtr.Zero;
        }
    }

    private void StopPreview()
    {
        if (_preview == IntPtr.Zero)
            return;

        CastorNative.PreviewStop(_preview);
        _isStarted = false;
        _previousSourceKey = IntPtr.Zero;
    }

    private void ResizePreview()
    {
        if (_preview == IntPtr.Zero || _hwnd == IntPtr.Zero)
            return;

        var scale = VisualRoot?.RenderScaling ?? 1.0;
        var width = Math.Max(1, (int)Math.Round(Bounds.Width * scale));
        var height = Math.Max(1, (int)Math.Round(Bounds.Height * scale));

        NativeMethods.SetWindowPos(
            _hwnd,
            IntPtr.Zero,
            0,
            0,
            width,
            height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

        CastorNative.PreviewResize(_preview, width, height);
    }

    private static CaptureSourceInfo? GetVideoSourceInfo(SceneItem? scene)
    {
        var source = scene?.Sources.FirstOrDefault(item => item.Kind == SourceKind.Video);
        if (source == null)
            return null;

        if (source.NativeDescriptor is CaptureSourceInfo info)
            return info;

        if (source.NativeDescriptor is FileSourceInfo fileInfo)
        {
            return new CaptureSourceInfo
            {
                Label = source.Name,
                Type = CaptureSourceType.File,
                SymbolicLink = fileInfo.FilePath,
                Index = source.Loop ? 1 : 0,
            };
        }

        return null;
    }

    private static IntPtr GetSourceKey(CaptureSourceInfo source)
    {
        return source.Type switch
        {
            CaptureSourceType.Window => source.Hwnd,
            CaptureSourceType.Monitor => source.HMonitor,
            _ => new IntPtr(StringComparer.Ordinal.GetHashCode($"{source.Type}:{source.SymbolicLink}:{source.Index}")),
        };
    }

    private static class NativeMethods
    {
        public const int SWP_NOZORDER = 0x0004;
        public const int SWP_NOACTIVATE = 0x0010;

        private const int CS_HREDRAW = 0x0002;
        private const int CS_VREDRAW = 0x0001;
        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;
        private const int IDC_ARROW = 32512;
        private static readonly string ClassName = "CastorDirectXPreviewHost";
        private static readonly WndProc WndProcDelegate = DefWindowProc;
        private static ushort _classAtom;

        public static void EnsureWindowClass()
        {
            if (_classAtom != 0)
                return;

            var wc = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                style = CS_HREDRAW | CS_VREDRAW,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProcDelegate),
                hInstance = GetModuleHandle(null),
                hCursor = LoadCursor(IntPtr.Zero, IDC_ARROW),
                lpszClassName = ClassName,
            };

            _classAtom = RegisterClassEx(ref wc);
            if (_classAtom == 0)
            {
                var error = Marshal.GetLastWin32Error();
                if (error != 1410)
                    throw new InvalidOperationException($"RegisterClassEx failed: {error}");
            }
        }

        public static IntPtr CreatePreviewWindow(IntPtr parent)
        {
            var hwnd = CreateWindowEx(
                0,
                ClassName,
                string.Empty,
                WS_CHILD | WS_VISIBLE,
                0,
                0,
                1,
                1,
                parent,
                IntPtr.Zero,
                GetModuleHandle(null),
                IntPtr.Zero);

            if (hwnd == IntPtr.Zero)
                throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");

            return hwnd;
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(
            int dwExStyle,
            string lpClassName,
            string lpWindowName,
            int dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll")]
        public static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            int uFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public int cbSize;
            public int style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }
    }
}
