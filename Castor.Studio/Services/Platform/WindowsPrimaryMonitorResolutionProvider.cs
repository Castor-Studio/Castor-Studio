using System.Runtime.InteropServices;
using CastorApplication.Models.Settings;

namespace CastorApplication.Services.Platform;

internal sealed class WindowsPrimaryMonitorResolutionProvider : IPrimaryMonitorResolutionProvider
{
    internal static readonly VideoCanvasResolution FallbackResolution = new(1920, 1080);

    private readonly IWindowsDisplayApi _displayApi;
    private readonly bool _isWindows;

    public WindowsPrimaryMonitorResolutionProvider()
        : this(new WindowsDisplayApi(), OperatingSystem.IsWindows())
    {
    }

    internal WindowsPrimaryMonitorResolutionProvider(IWindowsDisplayApi displayApi)
        : this(displayApi, true)
    {
    }

    internal WindowsPrimaryMonitorResolutionProvider(IWindowsDisplayApi displayApi, bool isWindows)
    {
        _displayApi = displayApi;
        _isWindows = isWindows;
    }

    public VideoCanvasResolution GetPrimaryMonitorResolution()
    {
        if (!_isWindows) return FallbackResolution;

        try
        {
            var primary = _displayApi.EnumerateMonitors()
                .FirstOrDefault(monitor => monitor.IsPrimary);
            if (primary == null) return FallbackResolution;

            var resolution = _displayApi.GetCurrentSettings(primary.DeviceName);
            return resolution is { IsValid: true } ? resolution.Value : FallbackResolution;
        }
        catch
        {
            return FallbackResolution;
        }
    }

    internal sealed record MonitorDescriptor(string DeviceName, bool IsPrimary);

    internal interface IWindowsDisplayApi
    {
        IReadOnlyList<MonitorDescriptor> EnumerateMonitors();
        VideoCanvasResolution? GetCurrentSettings(string deviceName);
    }

    private sealed class WindowsDisplayApi : IWindowsDisplayApi
    {
        private const uint MonitorInfoPrimary = 1;
        private const int CurrentSettings = -1;

        public IReadOnlyList<MonitorDescriptor> EnumerateMonitors()
        {
            var monitors = new List<MonitorDescriptor>();
            MonitorEnumProc callback = (
                IntPtr monitor,
                IntPtr deviceContext,
                ref Rect monitorRect,
                IntPtr data) =>
            {
                var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
                if (GetMonitorInfo(monitor, ref info))
                {
                    monitors.Add(new(
                        info.DeviceName ?? string.Empty,
                        (info.Flags & MonitorInfoPrimary) != 0));
                }

                return true;
            };

            if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
                return [];

            return monitors;
        }

        public VideoCanvasResolution? GetCurrentSettings(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return null;

            var mode = new DevMode
            {
                DeviceName = string.Empty,
                FormName = string.Empty,
                Size = (short)Marshal.SizeOf<DevMode>()
            };
            if (!EnumDisplaySettings(deviceName, CurrentSettings, ref mode)) return null;

            return new VideoCanvasResolution(mode.PelsWidth, mode.PelsHeight);
        }

        private delegate bool MonitorEnumProc(
            IntPtr monitor,
            IntPtr deviceContext,
            ref Rect monitorRect,
            IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumDisplayMonitors(
            IntPtr deviceContext,
            IntPtr clipRect,
            MonitorEnumProc callback,
            IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(
            IntPtr monitor,
            ref MonitorInfoEx monitorInfo);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumDisplaySettings(
            string deviceName,
            int modeNumber,
            ref DevMode devMode);

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MonitorInfoEx
        {
            public int Size;
            public Rect Monitor;
            public Rect Work;
            public uint Flags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string? DeviceName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DevMode
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;

            public short SpecVersion;
            public short DriverVersion;
            public short Size;
            public short DriverExtra;
            public int Fields;
            public int PositionX;
            public int PositionY;
            public int DisplayOrientation;
            public int DisplayFixedOutput;
            public short Color;
            public short Duplex;
            public short YResolution;
            public short TTOption;
            public short Collate;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string FormName;

            public short LogPixels;
            public int BitsPerPel;
            public int PelsWidth;
            public int PelsHeight;
            public int DisplayFlags;
            public int DisplayFrequency;
            public int IcmMethod;
            public int IcmIntent;
            public int MediaType;
            public int DitherType;
            public int Reserved1;
            public int Reserved2;
            public int PanningWidth;
            public int PanningHeight;
        }
    }
}
