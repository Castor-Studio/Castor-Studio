using CastorApplication.Models.Studio;
using CastorApplication.ViewModels.Scenes;

namespace Castor.Studio.Tests;

// The Centrer and Ajuster buttons of the Scenes toolbar.
public sealed class CompositionCanvasFitTests
{
    private static SourceTransform Source(double x, double y, double width, double height, double rotation = 0) =>
        new(Guid.NewGuid(), x, y, width, height, 1, 1, SourceCrop.None, (int)width, (int)height, true, rotation);

    [Fact]
    public void Centering_moves_the_source_without_resizing_it()
    {
        var placement = CompositionGeometry.CenterOnCanvas(Source(0, 0, 640, 360), 1920, 1080);

        Assert.Equal(640, placement.X, 3);
        Assert.Equal(360, placement.Y, 3);
        Assert.Equal(1, placement.ScaleX, 3);
    }

    [Fact]
    public void Fitting_fills_the_limiting_side_and_keeps_the_proportions()
    {
        var placement = CompositionGeometry.FitToCanvas(Source(10, 20, 800, 800), 1920, 1080);

        Assert.Equal(1.35, placement.ScaleX, 3);
        Assert.Equal(1.35, placement.ScaleY, 3);
        Assert.Equal(420, placement.X, 3);
        Assert.Equal(0, placement.Y, 3);
    }

    [Fact]
    public void A_rotated_source_fits_whole_inside_the_canvas()
    {
        var source = Source(0, 0, 1600, 900, 90);

        var fitted = CompositionGeometry.Preview(source, CompositionGeometry.FitToCanvas(source, 1920, 1080));
        var (left, top, right, bottom) = CompositionGeometry.Bounds(fitted);

        Assert.Equal(0, top, 3);
        Assert.Equal(1080, bottom, 3);
        Assert.True(left >= 0 && right <= 1920);
    }
}
