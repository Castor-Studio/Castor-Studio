using CastorApplication.Models.Studio;
using CastorApplication.ViewModels.Scenes;

namespace Castor.Studio.Tests;

public sealed class CompositionGeometryTests
{
    // Une source 1920×1080 réduite de moitié, posée en (100, 50) : 960×540 dans le canvas.
    private static readonly SourceTransform HalfSize = new(
        Guid.NewGuid(), 100, 50, 960, 540, 0.5, 0.5, SourceCrop.None, 1920, 1080, true);

    [Fact]
    public void Moving_shifts_the_position_and_nothing_else()
    {
        var placement = CompositionGeometry.Move(HalfSize, 30, -20);

        Assert.Equal(new SourcePlacement(130, 30, 0.5, 0.5, SourceCrop.None), placement);
    }

    [Fact]
    public void Pulling_the_right_side_stretches_only_the_width()
    {
        var placement = CompositionGeometry.Resize(HalfSize, CompositionHandle.Right, 960, 400, keepAspectRatio: true);

        // Un côté n'étire que son axe, même quand les proportions sont gardées.
        Assert.Equal((100d, 50d), (placement.X, placement.Y));
        Assert.Equal((1.0, 0.5), (placement.ScaleX, placement.ScaleY));
    }

    [Fact]
    public void Pulling_the_top_left_corner_keeps_the_bottom_right_one_in_place()
    {
        var placement = CompositionGeometry.Resize(HalfSize, CompositionHandle.TopLeft, 480, 270, keepAspectRatio: true);
        var result = CompositionGeometry.Preview(HalfSize, placement);

        Assert.Equal((480d, 270d), (result.Width, result.Height));
        Assert.Equal((HalfSize.X + HalfSize.Width, HalfSize.Y + HalfSize.Height),
            (result.X + result.Width, result.Y + result.Height));
    }

    [Fact]
    public void A_corner_keeps_the_proportions_by_following_the_side_pulled_the_most()
    {
        var placement = CompositionGeometry.Resize(HalfSize, CompositionHandle.BottomRight, 960, 10, keepAspectRatio: true);

        Assert.Equal(1.0, placement.ScaleX, 6);
        Assert.Equal(placement.ScaleX, placement.ScaleY, 6);
    }

    [Fact]
    public void A_corner_can_stretch_freely_when_asked()
    {
        var placement = CompositionGeometry.Resize(HalfSize, CompositionHandle.BottomRight, 960, 0, keepAspectRatio: false);

        Assert.Equal((1.0, 0.5), (placement.ScaleX, placement.ScaleY));
    }

    [Fact]
    public void A_source_cannot_be_shrunk_past_its_handles_nor_flipped()
    {
        var placement = CompositionGeometry.Resize(HalfSize, CompositionHandle.Left, 5000, 0, keepAspectRatio: false);
        var result = CompositionGeometry.Preview(HalfSize, placement);

        Assert.True(placement.ScaleX > 0);
        Assert.Equal(CompositionGeometry.MinimumExtent, result.Width, 6);
        // Le bord opposé ne bouge pas, même contre la butée.
        Assert.Equal(HalfSize.X + HalfSize.Width, result.X + result.Width, 6);
    }

    [Fact]
    public void Cropping_the_left_side_keeps_the_picture_in_place_under_the_edge()
    {
        // 100 pixels du canvas, à l'échelle 0.5, font 200 pixels de la source.
        var placement = CompositionGeometry.Crop(HalfSize, CompositionHandle.Left, 100, 0);
        var result = CompositionGeometry.Preview(HalfSize, placement);

        Assert.Equal(new SourceCrop(200, 0, 0, 0), placement.Crop);
        Assert.Equal((0.5, 0.5), (placement.ScaleX, placement.ScaleY));
        Assert.Equal(200d, result.X);
        Assert.Equal(HalfSize.X + HalfSize.Width, result.X + result.Width);
    }

    [Fact]
    public void Cropping_the_bottom_right_corner_takes_from_both_sides_it_touches()
    {
        var placement = CompositionGeometry.Crop(HalfSize, CompositionHandle.BottomRight, -50, -25);

        Assert.Equal(new SourceCrop(0, 0, 100, 50), placement.Crop);
        Assert.Equal((100d, 50d), (placement.X, placement.Y));
    }

    [Fact]
    public void A_crop_never_goes_negative_nor_takes_the_whole_source()
    {
        var uncropped = CompositionGeometry.Crop(HalfSize, CompositionHandle.Right, 500, 0);
        Assert.Equal(SourceCrop.None, uncropped.Crop);

        var everything = CompositionGeometry.Crop(HalfSize, CompositionHandle.Top, 0, 5000);
        Assert.Equal(1079, everything.Crop.Top);
    }

    [Fact]
    public void A_handle_is_found_on_either_side_of_the_edge_and_corners_win()
    {
        Assert.Equal(CompositionHandle.TopLeft, CompositionGeometry.HandleAt(HalfSize, 96, 54, tolerance: 6));
        Assert.Equal(CompositionHandle.Right, CompositionGeometry.HandleAt(HalfSize, 1064, 320, tolerance: 6));
        Assert.Null(CompositionGeometry.HandleAt(HalfSize, 500, 300, tolerance: 6));

        // Si petite que toutes ses poignées se recouvrent : c'est l'angle qui l'emporte.
        var tiny = HalfSize with { Width = 4, Height = 4 };
        Assert.Equal(CompositionHandle.TopLeft, CompositionGeometry.HandleAt(tiny, 101, 51, tolerance: 6));
    }
}
