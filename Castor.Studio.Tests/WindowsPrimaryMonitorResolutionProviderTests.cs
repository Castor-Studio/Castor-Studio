using CastorApplication.Models.Settings;
using CastorApplication.Services.Platform;

namespace Castor.Studio.Tests;

public sealed class WindowsPrimaryMonitorResolutionProviderTests
{
    [Fact]
    public void Native_provider_returns_a_valid_resolution_on_windows()
    {
        if (!OperatingSystem.IsWindows()) return;

        var resolution = new WindowsPrimaryMonitorResolutionProvider()
            .GetPrimaryMonitorResolution();

        Assert.True(resolution.IsValid);
    }

    [Fact]
    public void Primary_monitor_uses_the_physical_current_display_mode()
    {
        var api = new FakeDisplayApi
        {
            Monitors =
            [
                new("\\\\.\\DISPLAY2", false),
                new("\\\\.\\DISPLAY1", true)
            ],
            Settings = new()
            {
                ["\\\\.\\DISPLAY1"] = new VideoCanvasResolution(3840, 2160),
                ["\\\\.\\DISPLAY2"] = new VideoCanvasResolution(1920, 1080)
            }
        };

        var provider = new WindowsPrimaryMonitorResolutionProvider(api, isWindows: true);

        Assert.Equal(new VideoCanvasResolution(3840, 2160), provider.GetPrimaryMonitorResolution());
    }

    [Fact]
    public void Detection_failure_uses_the_safe_fallback()
    {
        var provider = new WindowsPrimaryMonitorResolutionProvider(
            new FakeDisplayApi { ThrowOnEnumeration = true },
            isWindows: true);

        Assert.Equal(new VideoCanvasResolution(1920, 1080), provider.GetPrimaryMonitorResolution());
    }

    [Fact]
    public void Invalid_physical_dimensions_use_the_safe_fallback_without_dpi_scaling()
    {
        var api = new FakeDisplayApi
        {
            Monitors = [new("\\\\.\\DISPLAY1", true)],
            Settings = new()
            {
                ["\\\\.\\DISPLAY1"] = new VideoCanvasResolution(0, 0)
            }
        };

        var provider = new WindowsPrimaryMonitorResolutionProvider(api, isWindows: true);

        Assert.Equal(new VideoCanvasResolution(1920, 1080), provider.GetPrimaryMonitorResolution());
    }

    private sealed class FakeDisplayApi : WindowsPrimaryMonitorResolutionProvider.IWindowsDisplayApi
    {
        public IReadOnlyList<WindowsPrimaryMonitorResolutionProvider.MonitorDescriptor> Monitors { get; set; } = [];
        public Dictionary<string, VideoCanvasResolution?> Settings { get; set; } = [];
        public bool ThrowOnEnumeration { get; set; }

        public IReadOnlyList<WindowsPrimaryMonitorResolutionProvider.MonitorDescriptor> EnumerateMonitors()
        {
            if (ThrowOnEnumeration) throw new InvalidOperationException("enumeration failed");
            return Monitors;
        }

        public VideoCanvasResolution? GetCurrentSettings(string deviceName) =>
            Settings.TryGetValue(deviceName, out var resolution) ? resolution : null;
    }
}
