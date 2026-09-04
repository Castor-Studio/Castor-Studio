using CastorApplication.Models.Settings;
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
