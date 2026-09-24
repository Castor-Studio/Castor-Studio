using CastorApplication.Services.Studio;

namespace Castor.Studio.Tests;

public sealed class ObsPreviewGraphicsTests
{
    [Fact]
    public void A_frame_stays_inside_the_rectangle_of_its_source()
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
    public void A_source_thinner_than_its_frame_is_filled_rather_than_framed()
    {
        // Deux bords qui se recouvrent dessineraient un cadre plus épais que la source.
        var edge = Assert.Single(ObsPreviewGraphics.BoxEdges(x: 10, y: 20, width: 6, height: 300, thickness: 4));

        Assert.Equal(new PreviewFillRect(10, 20, 6, 300), edge);
    }

    [Fact]
    public void The_chosen_source_gets_eight_handles_on_its_corners_and_its_sides()
    {
        var handles = ObsPreviewGraphics.HandleRects(x: 100, y: 50, width: 640, height: 360, size: 10);

        // Dans le sens des aiguilles d'une montre depuis le coin haut-gauche, et chacune
        // centrée sur son point : à cheval sur le bord, donc saisissable des deux côtés.
        Assert.Equal(
        [
            new PreviewFillRect(95, 45, 10, 10),
            new PreviewFillRect(415, 45, 10, 10),
            new PreviewFillRect(735, 45, 10, 10),
            new PreviewFillRect(735, 225, 10, 10),
            new PreviewFillRect(735, 405, 10, 10),
            new PreviewFillRect(415, 405, 10, 10),
            new PreviewFillRect(95, 405, 10, 10),
            new PreviewFillRect(95, 225, 10, 10)
        ], handles);
    }

    [Fact]
    public void The_core_of_a_handle_sits_at_the_centre_of_the_handle()
    {
        var handle = ObsPreviewGraphics.HandleRects(x: 0, y: 0, width: 640, height: 360, size: 10)[0];
        var core = ObsPreviewGraphics.HandleRects(x: 0, y: 0, width: 640, height: 360, size: 5)[0];

        // Le cœur clair s'inscrit dans le carré d'accent : c'est le même point, deux tailles.
        Assert.Equal(handle.X + handle.Width / 2, core.X + core.Width / 2);
        Assert.Equal(handle.Y + handle.Height / 2, core.Y + core.Height / 2);
    }

    [Fact]
    public void A_source_the_engine_composes_to_nothing_gets_no_overlay()
    {
        Assert.Empty(ObsPreviewGraphics.BoxEdges(x: 0, y: 0, width: 0, height: 360, thickness: 2));
        Assert.Empty(ObsPreviewGraphics.BoxEdges(x: 0, y: 0, width: 640, height: 360, thickness: 0));
        Assert.Empty(ObsPreviewGraphics.HandleRects(x: 0, y: 0, width: 640, height: 0, size: 10));
        Assert.Empty(ObsPreviewGraphics.HandleRects(x: 0, y: 0, width: 640, height: 360, size: 0));
    }

    [Fact]
    public void The_overlay_keeps_the_same_size_on_screen_whatever_the_panel()
    {
        var full = ObsPreviewGraphics.MetricsFor(viewportWidth: 1920, canvasWidth: 1920);
        var half = ObsPreviewGraphics.MetricsFor(viewportWidth: 960, canvasWidth: 1920);

        // Le canvas réduit de moitié : tout double en pixels du canvas pour occuper la même
        // place à l'écran. Une poignée se vise à la souris, elle ne suit pas l'échelle.
        Assert.Equal(full.Handle * 2, half.Handle);
        Assert.Equal(full.HandleCore * 2, half.HandleCore);
        Assert.Equal(full.ChosenThickness * 2, half.ChosenThickness);
    }

    [Fact]
    public void A_chosen_source_is_heavier_than_the_others()
    {
        var metrics = ObsPreviewGraphics.MetricsFor(viewportWidth: 1920, canvasWidth: 1920);

        // La couleur porte l'état, mais le trait choisi reste plus épais : sur une image
        // bleue, la seule couleur ne suffirait pas à le distinguer.
        Assert.True(metrics.ChosenThickness > metrics.IdleThickness);
        Assert.True(metrics.HandleCore < metrics.Handle);
    }

    [Fact]
    public void A_viewport_that_has_no_size_yet_falls_back_rather_than_dividing_by_zero()
    {
        var metrics = ObsPreviewGraphics.MetricsFor(viewportWidth: 0, canvasWidth: 0);

        Assert.True(metrics.Handle > 0);
        Assert.True(metrics.ChosenThickness > 0);
    }
}
