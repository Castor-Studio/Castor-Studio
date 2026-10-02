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

    // La même source tournée d'un quart de tour : son origine reste en (100, 50), son bord
    // haut descend le long de l'axe vertical du canvas.
    private static readonly SourceTransform QuarterTurn = HalfSize with { Rotation = 90 };

    [Fact]
    public void Turning_by_hand_keeps_the_centre_where_it_is()
    {
        var centre = CompositionGeometry.Center(HalfSize);
        Assert.Equal((580d, 320d), centre);

        // Le pointeur part à droite du centre et finit en dessous : un quart de tour dans le
        // sens des aiguilles d'une montre.
        var placement = CompositionGeometry.Rotate(HalfSize, 1000, 320, 580, 900, constrain: false);
        var result = CompositionGeometry.Preview(HalfSize, placement);

        Assert.Equal(90, placement.Rotation, 6);
        var (x, y) = CompositionGeometry.Center(result);
        Assert.Equal(580, x, 6);
        Assert.Equal(320, y, 6);
    }

    [Fact]
    public void A_constrained_turn_lands_on_steps_of_fifteen_degrees()
    {
        // Environ 20° parcourus autour du centre.
        var angle = 20 * Math.PI / 180;
        var placement = CompositionGeometry.Rotate(HalfSize, 680, 320,
            580 + 100 * Math.Cos(angle), 320 + 100 * Math.Sin(angle), constrain: true);

        Assert.Equal(15, placement.Rotation, 6);
    }

    [Fact]
    public void Quarter_turns_add_up_and_a_full_turn_comes_back_to_nothing()
    {
        Assert.Equal(90, CompositionGeometry.RotateBy(HalfSize, 90).Rotation, 6);
        Assert.Equal(-90, CompositionGeometry.RotateBy(HalfSize, -90).Rotation, 6);
        Assert.Equal(180, CompositionGeometry.RotateBy(QuarterTurn, 90).Rotation, 6);
        Assert.Equal(0, CompositionGeometry.RotateBy(QuarterTurn, 270).Rotation, 6);
    }

    [Fact]
    public void A_turned_source_is_hit_where_it_is_drawn_not_where_it_was()
    {
        // Tournée autour de (100, 50), la source occupe x ∈ [-440, 100], y ∈ [50, 1010].
        Assert.True(CompositionGeometry.Contains(QuarterTurn, 0, 500));
        Assert.False(CompositionGeometry.Contains(QuarterTurn, 500, 300));

        // Son coin bas-droit est passé en bas à gauche de l'origine.
        Assert.Equal(CompositionHandle.BottomRight, CompositionGeometry.HandleAt(QuarterTurn, -440, 1010, tolerance: 6));
    }

    [Fact]
    public void Pulling_the_right_side_of_a_turned_source_follows_its_own_axis()
    {
        // Le bord droit d'une source tournée d'un quart de tour pointe vers le bas du canvas.
        var placement = CompositionGeometry.Resize(QuarterTurn, CompositionHandle.Right, 0, 960, keepAspectRatio: true);

        Assert.Equal((1.0, 0.5), (placement.ScaleX, placement.ScaleY));
        Assert.Equal((100d, 50d), (placement.X, placement.Y));
    }

    [Fact]
    public void Cropping_the_left_side_of_a_turned_source_moves_its_origin_along_that_side()
    {
        // 100 pixels du canvas vers le bas, le long du bord haut de la source tournée.
        var placement = CompositionGeometry.Crop(QuarterTurn, CompositionHandle.Left, 0, 100);

        Assert.Equal(new SourceCrop(200, 0, 0, 0), placement.Crop);
        Assert.Equal(100, placement.X, 6);
        Assert.Equal(150, placement.Y, 6);
    }

    [Fact]
    public void Resetting_the_crop_leaves_what_was_visible_in_place()
    {
        var cropped = HalfSize with { X = 200, Y = 100, Crop = new SourceCrop(200, 100, 0, 0) };
        var placement = CompositionGeometry.ResetCrop(cropped);

        Assert.Equal(SourceCrop.None, placement.Crop);
        Assert.Equal((100d, 50d), (placement.X, placement.Y));
    }

    [Fact]
    public void Rotation_zones_sit_outside_the_corners_only()
    {
        // Juste au-delà du coin haut-gauche, en diagonale.
        Assert.Equal(CompositionHandle.TopLeft, CompositionGeometry.RotationCornerAt(HalfSize, 90, 40, reach: 20));
        // Dans la source : c'est un déplacement, pas une rotation.
        Assert.Null(CompositionGeometry.RotationCornerAt(HalfSize, 110, 60, reach: 20));
        // Loin de tout coin.
        Assert.Null(CompositionGeometry.RotationCornerAt(HalfSize, 580, 20, reach: 20));
    }

    [Fact]
    public void A_source_dragged_near_the_centre_of_the_canvas_lands_on_it()
    {
        // HalfSize fait 960×540 : centré dans un canvas 1920×1080, son origine est en (480, 270).
        var moved = CompositionGeometry.Move(HalfSize, 385, 216);
        var (placement, guides) = CompositionGeometry.Snap(HalfSize, moved, [], 1920, 1080, reach: 10);

        Assert.Equal((480d, 270d), (placement.X, placement.Y));
        Assert.Contains(new CompositionGuide(IsVertical: true, 960), guides);
        Assert.Contains(new CompositionGuide(IsVertical: false, 540), guides);
    }

    [Fact]
    public void A_source_lines_up_with_the_edges_of_another()
    {
        var other = HalfSize with { SourceId = Guid.NewGuid(), X = 1200, Y = 700, Width = 400, Height = 300 };
        // Le bord droit de HalfSize (1060) arrive à 6 pixels du bord gauche de l'autre (1200).
        var moved = CompositionGeometry.Move(HalfSize, 134, 0);
        var (placement, guides) = CompositionGeometry.Snap(HalfSize, moved, [other], 0, 0, reach: 10);

        Assert.Equal(240d, placement.X);
        Assert.Equal(new CompositionGuide(IsVertical: true, 1200), Assert.Single(guides));
    }

    [Fact]
    public void Nothing_close_enough_leaves_the_source_where_the_pointer_put_it()
    {
        var moved = CompositionGeometry.Move(HalfSize, 37, 23);
        var (placement, guides) = CompositionGeometry.Snap(HalfSize, moved, [], 1920, 1080, reach: 10);

        Assert.Equal(moved, placement);
        Assert.Empty(guides);
    }

    [Fact]
    public void A_turned_source_snaps_by_the_box_that_holds_it()
    {
        var (left, top, right, bottom) = CompositionGeometry.Bounds(QuarterTurn);

        Assert.Equal(-440, left, 6);
        Assert.Equal(50, top, 6);
        Assert.Equal(100, right, 6);
        Assert.Equal(1010, bottom, 6);
    }

    [Fact]
    public void A_handle_pulls_in_the_direction_its_source_is_turned()
    {
        Assert.Equal(0, CompositionGeometry.PullDirection(CompositionHandle.Right, 0), 6);
        Assert.Equal(90, CompositionGeometry.PullDirection(CompositionHandle.Right, 90), 6);
        Assert.Equal(135, CompositionGeometry.PullDirection(CompositionHandle.BottomRight, 90), 6);
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
