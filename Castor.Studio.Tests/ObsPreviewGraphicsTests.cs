using CastorApplication.Services.Studio;

namespace Castor.Studio.Tests;

public sealed class ObsPreviewGraphicsTests
{
    [Fact]
    public void A_source_is_marked_by_four_corner_brackets_inside_its_rectangle()
    {
        var marks = ObsPreviewGraphics.CornerMarks(x: 100, y: 50, width: 640, height: 360, thickness: 2, arm: 20);

        // Deux branches par angle, dans le sens des aiguilles depuis le coin haut-gauche, et
        // vers l'intérieur : un trait posé à cheval ferait paraître la source plus grande.
        Assert.Equal(
        [
            new PreviewFillRect(100, 50, 20, 2),
            new PreviewFillRect(100, 50, 2, 20),
            new PreviewFillRect(720, 50, 20, 2),
            new PreviewFillRect(738, 50, 2, 20),
            new PreviewFillRect(720, 408, 20, 2),
            new PreviewFillRect(738, 390, 2, 20),
            new PreviewFillRect(100, 408, 20, 2),
            new PreviewFillRect(100, 390, 2, 20)
        ], marks);
    }

    [Fact]
    public void Corner_brackets_never_join_into_a_frame_on_a_small_source()
    {
        // Une branche ne dépasse pas le tiers du plus petit côté, sinon les quatre équerres
        // se rejoignent et redessinent le cadre qu'elles remplacent.
        var marks = ObsPreviewGraphics.CornerMarks(x: 0, y: 0, width: 30, height: 30, thickness: 2, arm: 20);

        Assert.All(marks, mark => Assert.True(Math.Max(mark.Width, mark.Height) <= 10));
    }

    [Fact]
    public void The_chosen_source_gets_one_bar_centred_on_each_side()
    {
        var marks = ObsPreviewGraphics.EdgeMarks(x: 100, y: 50, width: 640, height: 360, thickness: 2, bar: 20);

        // Haut, droite, bas, gauche. Une barre est couchée sur son bord : sa forme dit le
        // geste attendu.
        Assert.Equal(
        [
            new PreviewFillRect(410, 50, 20, 2),
            new PreviewFillRect(738, 220, 2, 20),
            new PreviewFillRect(410, 408, 20, 2),
            new PreviewFillRect(100, 220, 2, 20)
        ], marks);
    }

    [Fact]
    public void The_frame_of_the_chosen_source_is_cut_into_teeth_all_around()
    {
        var teeth = ObsPreviewGraphics.DashedFrame(x: 0, y: 0, width: 100, height: 50, thickness: 2, dash: 25);

        // Haut et bas sur toute la largeur, côtés entre les deux : quatre dents en haut,
        // deux à droite, quatre en bas, deux à gauche.
        Assert.Equal(12, teeth.Count);
        Assert.Equal(new PreviewFillRect(0, 0, 25, 2), teeth[0]);
        Assert.Equal(new PreviewFillRect(75, 0, 25, 2), teeth[3]);
        Assert.Equal(new PreviewFillRect(98, 2, 2, 25), teeth[4]);
        Assert.Equal(new PreviewFillRect(0, 48, 25, 2), teeth[6]);
        Assert.Equal(new PreviewFillRect(0, 27, 2, 21), teeth[11]);

        // Les dents se touchent : le cadre est continu, c'est leur couleur qui alterne.
        var top = teeth.Take(4).ToList();
        Assert.Equal(100f, top.Sum(tooth => tooth.Width));
    }

    [Fact]
    public void A_source_thinner_than_its_frame_is_filled_rather_than_framed()
    {
        // Deux bords qui se recouvrent dessineraient un cadre plus épais que la source.
        var tooth = Assert.Single(
            ObsPreviewGraphics.DashedFrame(x: 10, y: 20, width: 6, height: 300, thickness: 4, dash: 25));

        Assert.Equal(new PreviewFillRect(10, 20, 6, 300), tooth);
    }

    [Fact]
    public void A_source_the_engine_composes_to_nothing_gets_no_mark()
    {
        Assert.Empty(ObsPreviewGraphics.CornerMarks(x: 0, y: 0, width: 0, height: 360, thickness: 2, arm: 20));
        Assert.Empty(ObsPreviewGraphics.EdgeMarks(x: 0, y: 0, width: 640, height: 0, thickness: 2, bar: 20));
        Assert.Empty(ObsPreviewGraphics.DashedFrame(x: 0, y: 0, width: 640, height: 360, thickness: 2, dash: 0));
    }

    [Fact]
    public void The_overlay_keeps_the_same_size_on_screen_whatever_the_panel()
    {
        var full = ObsPreviewGraphics.MetricsFor(viewportWidth: 1920, canvasWidth: 1920);
        var half = ObsPreviewGraphics.MetricsFor(viewportWidth: 960, canvasWidth: 1920);

        // Le canvas réduit de moitié : tout double en pixels du canvas pour occuper la même
        // place à l'écran. Une marque se vise à la souris, elle ne suit pas l'échelle.
        Assert.Equal(full.MarkArm * 2, half.MarkArm);
        Assert.Equal(full.Dash * 2, half.Dash);
        Assert.Equal(full.MarkThickness * 2, half.MarkThickness);
    }

    [Fact]
    public void A_viewport_that_has_no_size_yet_falls_back_rather_than_dividing_by_zero()
    {
        var metrics = ObsPreviewGraphics.MetricsFor(viewportWidth: 0, canvasWidth: 0);

        Assert.True(metrics.MarkArm > 0);
        Assert.True(metrics.Dash > 0);
    }
}
