using CastorApplication.Models.Studio;
using CastorApplication.Services.Studio;

namespace Castor.Studio.Tests;

public sealed class ObsPreviewGraphicsTests
{
    [Fact]
    public void A_frame_stays_inside_the_rectangle_of_its_source()
    {
        var edges = ObsPreviewGraphics.BoxEdges(x: 100, y: 50, width: 640, height: 360, thickness: 4);

        // Four edges, drawn inwards: a frame straddling the edge would make the source look larger
        // than it is.
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
        // Two overlapping edges would draw a frame thicker than the source.
        var edge = Assert.Single(ObsPreviewGraphics.BoxEdges(x: 10, y: 20, width: 6, height: 300, thickness: 4));

        Assert.Equal(new PreviewFillRect(10, 20, 6, 300), edge);
    }

    [Fact]
    public void The_chosen_source_gets_eight_handles_on_its_corners_and_its_sides()
    {
        var handles = ObsPreviewGraphics.HandleRects(x: 100, y: 50, width: 640, height: 360, size: 10);

        // Clockwise from the top-left corner, each centered on its point: straddling the edge, so
        // grabbable from both sides.
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

        // The light core sits inside the accent square: same point, two sizes.
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

        // The canvas halved: everything doubles in canvas pixels to take the same room on screen.
        // A handle is aimed with the mouse, it does not follow the scale.
        Assert.Equal(full.Handle * 2, half.Handle);
        Assert.Equal(full.HandleCore * 2, half.HandleCore);
        Assert.Equal(full.ChosenThickness * 2, half.ChosenThickness);
    }

    [Fact]
    public void A_chosen_source_is_heavier_than_the_others()
    {
        var metrics = ObsPreviewGraphics.MetricsFor(viewportWidth: 1920, canvasWidth: 1920);

        // Color carries the state, but the selected stroke stays thicker: on a blue image, color
        // alone would not set it apart.
        Assert.True(metrics.ChosenThickness > metrics.IdleThickness);
        Assert.True(metrics.HandleCore < metrics.Handle);
    }

    [Fact]
    public void Crop_brackets_sit_inside_the_source_at_its_corners_and_middles()
    {
        var brackets = ObsPreviewGraphics.CropBrackets(width: 640, height: 360, arm: 16, thickness: 4);

        // Four two-stroke brackets, four one-stroke bars.
        Assert.Equal(12, brackets.Count);
        Assert.All(brackets, rect =>
        {
            Assert.InRange(rect.X, 0, 640);
            Assert.InRange(rect.X + rect.Width, 0, 640);
            Assert.InRange(rect.Y, 0, 360);
            Assert.InRange(rect.Y + rect.Height, 0, 360);
        });
        Assert.Contains(new PreviewFillRect(0, 0, 16, 4), brackets);
        Assert.Contains(new PreviewFillRect(636, 172, 4, 16), brackets);
    }

    [Fact]
    public void Crop_brackets_never_cross_on_a_small_source()
    {
        var brackets = ObsPreviewGraphics.CropBrackets(width: 30, height: 30, arm: 16, thickness: 4);

        Assert.All(brackets, rect => Assert.True(Math.Max(rect.Width, rect.Height) <= 10));
    }

    [Fact]
    public void A_dashed_outline_alternates_dashes_and_gaps_around_the_whole_box()
    {
        var dashes = ObsPreviewGraphics.DashedEdges(x: -100, y: 0, width: 60, height: 20, thickness: 1, dash: 10);

        // Three dashes per long side, one per short side, each doubled.
        Assert.Equal(3 * 2 + 1 * 2, dashes.Count);
        Assert.Contains(new PreviewFillRect(-100, 0, 10, 1), dashes);
        Assert.Contains(new PreviewFillRect(-80, 19, 10, 1), dashes);
        Assert.DoesNotContain(dashes, rect => rect.X > -40);
    }

    [Fact]
    public void A_guide_crosses_the_whole_canvas_centred_on_its_line()
    {
        var rects = ObsPreviewGraphics.GuideRects(
            [new CompositionGuide(IsVertical: true, 960), new CompositionGuide(IsVertical: false, 540)],
            canvasWidth: 1920,
            canvasHeight: 1080,
            thickness: 2);

        Assert.Equal(
            [new PreviewFillRect(959, 0, 2, 1080), new PreviewFillRect(0, 539, 1920, 2)],
            rects);
        Assert.Empty(ObsPreviewGraphics.GuideRects([], 1920, 1080, 2));
    }

    [Fact]
    public void A_viewport_that_has_no_size_yet_falls_back_rather_than_dividing_by_zero()
    {
        var metrics = ObsPreviewGraphics.MetricsFor(viewportWidth: 0, canvasWidth: 0);

        Assert.True(metrics.Handle > 0);
        Assert.True(metrics.ChosenThickness > 0);
    }

    [Fact]
    public void Only_a_zoomed_source_with_room_for_it_carries_a_zoom_badge()
    {
        // The canvas is shown at half size: an 80 × 22 screen-pixel badge covers twice that.
        var metrics = ObsPreviewGraphics.MetricsFor(viewportWidth: 960, canvasWidth: 1920);
        var source = new SourceTransform(Guid.NewGuid(), 0, 0, 640, 360, 1, 1, SourceCrop.None, 640, 360, true);

        Assert.Null(ObsPreviewGraphics.ZoomBadge(source, 80, 22, metrics));

        var badge = ObsPreviewGraphics.ZoomBadge(source with { Zoom = new SourceZoom(2) }, 80, 22, metrics);
        Assert.Equal(new PreviewFillRect(metrics.BadgeInset, metrics.BadgeInset, 160, 44), badge);

        Assert.Null(ObsPreviewGraphics.ZoomBadge(
            source with { Width = 120, Height = 40, Zoom = new SourceZoom(2) }, 80, 22, metrics));
        Assert.StartsWith("Zoom ×", ObsPreviewGraphics.ZoomLabel(new SourceZoom(1.5)));
    }

    [Fact]
    public void The_zoom_map_shows_the_window_the_frame_shows_in_its_bottom_right_corner()
    {
        var metrics = ObsPreviewGraphics.MetricsFor(viewportWidth: 1920, canvasWidth: 1920);
        var source = new SourceTransform(Guid.NewGuid(), 0, 0, 1280, 720, 1, 1, SourceCrop.None, 1280, 720, true)
        {
            Zoom = new SourceZoom(2, 1, 0)
        };

        var shapes = ObsPreviewGraphics.ZoomMapShapes(source, metrics);

        Assert.NotNull(shapes);
        var plate = shapes.Value.Plate;
        Assert.Equal(1280 - 8, plate.X + plate.Width, 3);
        Assert.Equal(720 - 8, plate.Y + plate.Height, 3);
        // Pinned to the top right of the picture, the window covers half of it on each side.
        var window = shapes.Value.Window;
        Assert.Equal(plate.X + plate.Width / 2, window.X, 3);
        Assert.Equal(plate.Y, window.Y, 3);
        Assert.Equal(plate.Width / 2, window.Width, 3);
    }
}
