using Avalonia.Input;
using CastorApplication.Models.Studio;
using CastorApplication.ViewModels.Scenes;

namespace Castor.Studio.Tests;

public sealed class CompositionShortcutsTests
{
    [Theory]
    [InlineData(Key.D, KeyModifiers.Control, CompositionCommand.CenterSelected)]
    [InlineData(Key.F, KeyModifiers.Control, CompositionCommand.FitSelected)]
    [InlineData(Key.R, KeyModifiers.Control, CompositionCommand.ResetTransform)]
    [InlineData(Key.R, KeyModifiers.None, CompositionCommand.RotateRight)]
    [InlineData(Key.R, KeyModifiers.Shift, CompositionCommand.RotateLeft)]
    [InlineData(Key.C, KeyModifiers.None, CompositionCommand.ToggleCrop)]
    [InlineData(Key.Tab, KeyModifiers.None, CompositionCommand.SelectNext)]
    [InlineData(Key.Tab, KeyModifiers.Shift, CompositionCommand.SelectPrevious)]
    [InlineData(Key.PageUp, KeyModifiers.None, CompositionCommand.BringForward)]
    [InlineData(Key.PageDown, KeyModifiers.Control, CompositionCommand.SendToBack)]
    public void A_key_resolves_to_its_command(Key key, KeyModifiers modifiers, CompositionCommand expected)
    {
        Assert.Equal(expected, CompositionShortcuts.Resolve(key, modifiers)?.Command);
    }

    [Fact]
    public void Arrows_nudge_by_one_pixel_and_by_ten_with_shift()
    {
        Assert.Equal(new CompositionKeyAction(CompositionCommand.Nudge, -1, 0),
            CompositionShortcuts.Resolve(Key.Left, KeyModifiers.None));
        Assert.Equal(new CompositionKeyAction(CompositionCommand.Nudge, 0, 10),
            CompositionShortcuts.Resolve(Key.Down, KeyModifiers.Shift));
        // Ctrl+flèche n'est à personne ici : la touche poursuit son chemin.
        Assert.Null(CompositionShortcuts.Resolve(Key.Left, KeyModifiers.Control));
    }

    [Fact]
    public void Alt_holds_the_crop_and_changes_no_command()
    {
        // Alt enfoncé pour rogner, R tourne toujours la source.
        Assert.Equal(CompositionCommand.RotateRight, CompositionShortcuts.Resolve(Key.R, KeyModifiers.Alt)?.Command);
        Assert.Equal(new CompositionKeyAction(CompositionCommand.Nudge, 1, 0),
            CompositionShortcuts.Resolve(Key.Right, KeyModifiers.Alt));
    }

    [Fact]
    public void Zoom_follows_the_typed_character_whatever_the_layout()
    {
        // Sur un clavier AZERTY, « + » se tape avec Maj.
        Assert.Equal(CompositionCommand.ZoomIn, CompositionShortcuts.Resolve(Key.OemPlus, KeyModifiers.Shift, "+")?.Command);
        Assert.Equal(CompositionCommand.ZoomOut, CompositionShortcuts.Resolve(Key.Subtract, KeyModifiers.None)?.Command);
        Assert.Equal(CompositionCommand.ResetZoom, CompositionShortcuts.Resolve(Key.D0, KeyModifiers.Shift, "0")?.Command);
        Assert.Null(CompositionShortcuts.Resolve(Key.OemPlus, KeyModifiers.Control, "+"));
    }

    [Fact]
    public void Every_command_with_a_key_names_it()
    {
        foreach (var command in Enum.GetValues<CompositionCommand>())
            Assert.False(string.IsNullOrEmpty(CompositionShortcuts.KeysOf(command)));
    }

    [Fact]
    public void A_rotated_source_fits_by_the_box_that_holds_it()
    {
        // 640 × 360 tournée d'un quart de tour occupe 360 × 640 : c'est la hauteur qui borne.
        var source = new SourceTransform(Guid.NewGuid(), 500, 100, 640, 360, 1, 1, SourceCrop.None, 640, 360, true, 90);

        var fitted = CompositionGeometry.Preview(source, CompositionGeometry.Fit(source, 1920, 1080));
        var (left, top, right, bottom) = CompositionGeometry.Bounds(fitted);

        Assert.Equal(1080, bottom - top, 6);
        Assert.Equal(0, top, 6);
        Assert.Equal(960, (left + right) / 2, 6);
    }
}
