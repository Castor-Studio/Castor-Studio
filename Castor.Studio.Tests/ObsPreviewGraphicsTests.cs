using CastorApplication.Services.Studio;

namespace Castor.Studio.Tests;

public sealed class ObsPreviewGraphicsTests
{
    [Fact]
    public void An_outline_stays_inside_the_rectangle_of_its_source()
    {
        var edges = ObsPreviewGraphics.BoxEdges(x: 100, y: 50, width: 640, height: 360, thickness: 4);

        // Quatre bords, tracés vers l'intérieur : un cadre à cheval sur le bord ferait
        // paraître la source plus grande qu'elle n'est.
        Assert.Equal(4, edges.Count);
        Assert.Equal(new PreviewFillRect(100, 50, 640, 4), edges[0]);
        Assert.Equal(new PreviewFillRect(100, 406, 640, 4), edges[1]);
        Assert.Equal(new PreviewFillRect(100, 54, 4, 352), edges[2]);
        Assert.Equal(new PreviewFillRect(736, 54, 4, 352), edges[3]);

        Assert.All(edges, edge =>
        {
            Assert.InRange(edge.X, 100, 740);
            Assert.InRange(edge.Y, 50, 410);
            Assert.InRange(edge.X + edge.Width, 100, 740);
            Assert.InRange(edge.Y + edge.Height, 50, 410);
        });
    }

    [Fact]
    public void A_source_thinner_than_its_outline_is_filled_rather_than_framed()
    {
        // Deux bords qui se recouvrent dessineraient un cadre plus épais que la source.
        var edge = Assert.Single(ObsPreviewGraphics.BoxEdges(x: 10, y: 20, width: 6, height: 300, thickness: 4));

        Assert.Equal(new PreviewFillRect(10, 20, 6, 300), edge);
    }

    [Fact]
    public void A_source_the_engine_composes_to_nothing_has_no_outline()
    {
        Assert.Empty(ObsPreviewGraphics.BoxEdges(x: 0, y: 0, width: 0, height: 0, thickness: 4));
        Assert.Empty(ObsPreviewGraphics.BoxEdges(x: 0, y: 0, width: 640, height: 360, thickness: 0));
    }

    [Theory]
    // Le canvas réduit de moitié : un trait deux fois plus épais en pixels du canvas pour la
    // même épaisseur à l'écran.
    [InlineData(960, 1920u, 4f)]
    [InlineData(1920, 1920u, 2f)]
    // Un aperçu plus grand que le canvas ne descend pas sous le pixel.
    [InlineData(3840, 1920u, 1f)]
    public void The_outline_keeps_the_same_thickness_on_screen(int viewportWidth, uint canvasWidth, float expected) =>
        Assert.Equal(expected, ObsPreviewGraphics.OutlineThickness(viewportWidth, canvasWidth));

    [Fact]
    public void A_viewport_that_has_no_size_yet_falls_back_rather_than_dividing_by_zero() =>
        Assert.True(ObsPreviewGraphics.OutlineThickness(0, 0) > 0);
}
