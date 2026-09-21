using CastorApplication.Models.Settings;
using CastorApplication.Services.Platform;
using CastorApplication.Services.Settings;
using CastorApplication.Services.Studio;

namespace Castor.Studio.Tests;

public sealed class VideoResolutionTests
{
    [Theory]
    [InlineData(0, 3840, 2160)]
    [InlineData(1, 1920, 1080)]
    [InlineData(2, 1280, 720)]
    [InlineData(3, 2560, 1440)]
    public void Base_resolution_indices_remain_compatible(int index, int width, int height)
    {
        Assert.Equal((width, height), VideoResolution.BaseFromIndex(index));
    }

    [Fact]
    public void Base_options_put_the_primary_monitor_first_and_deduplicate_matching_presets()
    {
        var options = VideoResolution.BaseOptions(new VideoCanvasResolution(1920, 1080));

        Assert.Equal("1920x1080 (Moniteur principal)", options[0].Label);
        Assert.Equal(1, options[0].LegacyIndex);
        Assert.Equal(4, options.Count);
        Assert.DoesNotContain(options.Skip(1), option => option.Resolution == new VideoCanvasResolution(1920, 1080));
    }

    [Fact]
    public void A_non_standard_primary_monitor_is_available_as_a_canvas_option()
    {
        var options = VideoResolution.BaseOptions(new VideoCanvasResolution(3440, 1440));

        Assert.Equal(new VideoCanvasResolution(3440, 1440), options[0].Resolution);
        Assert.Null(options[0].LegacyIndex);
        Assert.Contains(options, option => option.LegacyIndex == 3);
    }

    [Fact]
    public void A_persisted_legacy_index_wins_over_monitor_detection()
    {
        var directory = Directory.CreateTempSubdirectory("castor-resolution-settings-");
        var settingsPath = Path.Combine(directory.FullName, "settings.json");
        try
        {
            var settingsService = new SettingsService(settingsPath);
            settingsService.Save(new ApplicationSettings { SelectedBaseResolutionIndex = 3 });
            var resolver = new VideoCanvasResolutionResolver(
                settingsService,
                new TestResolutionProvider(new VideoCanvasResolution(3440, 1440)));

            Assert.Equal(
                new VideoCanvasResolution(2560, 1440),
                resolver.Resolve(settingsService.Load()));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void A_persisted_custom_canvas_wins_over_both_legacy_index_and_monitor_detection()
    {
        var directory = Directory.CreateTempSubdirectory("castor-resolution-settings-");
        var settingsPath = Path.Combine(directory.FullName, "settings.json");
        try
        {
            var settingsService = new SettingsService(settingsPath);
            settingsService.Save(new ApplicationSettings
            {
                SelectedBaseResolutionIndex = 3,
                BaseCanvasWidth = 3440,
                BaseCanvasHeight = 1440
            });
            var resolver = new VideoCanvasResolutionResolver(
                settingsService,
                new TestResolutionProvider(new VideoCanvasResolution(1920, 1080)));

            Assert.Equal(
                new VideoCanvasResolution(3440, 1440),
                resolver.Resolve(settingsService.Load()));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Missing_settings_use_the_primary_monitor_resolution()
    {
        var directory = Directory.CreateTempSubdirectory("castor-resolution-settings-");
        try
        {
            var settingsService = new SettingsService(Path.Combine(directory.FullName, "settings.json"));
            var resolver = new VideoCanvasResolutionResolver(
                settingsService,
                new TestResolutionProvider(new VideoCanvasResolution(3440, 1440)));

            Assert.Equal(
                new VideoCanvasResolution(3440, 1440),
                resolver.Resolve(settingsService.Load()));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(0, 1920, 1080)]
    [InlineData(1, 1280, 720)]
    [InlineData(2, 854, 480)]
    [InlineData(3, 2560, 1440)]
    public void Output_resolution_indices_remain_compatible(int index, int width, int height)
    {
        Assert.Equal((width, height), VideoResolution.OutputFromIndex(index));
    }

    [Theory]
    [InlineData(1920, 1080, 2560, 1440, 0, 0, 1920, 1080)]
    [InlineData(1000, 1000, 2560, 1440, 0, 219, 1000, 562)]
    [InlineData(1000, 500, 1920, 1080, 56, 0, 888, 500)]
    public void Preview_viewport_fits_and_centers_the_whole_canvas(
        uint displayWidth,
        uint displayHeight,
        uint canvasWidth,
        uint canvasHeight,
        int x,
        int y,
        int width,
        int height)
    {
        Assert.Equal(
            new PreviewViewport(x, y, width, height),
            ObsPreviewGraphics.CalculateViewport(displayWidth, displayHeight, canvasWidth, canvasHeight));
    }
}
