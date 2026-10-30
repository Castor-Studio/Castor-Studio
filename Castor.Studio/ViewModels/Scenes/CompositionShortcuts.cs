using Avalonia.Input;

namespace CastorApplication.ViewModels.Scenes;

/// <summary>What a key, a button or a menu row asks of the canvas.</summary>
public enum CompositionCommand
{
    SelectNext,
    SelectPrevious,
    Nudge,
    CenterSelected,
    FitSelected,
    ResetTransform,
    RotateRight,
    RotateLeft,
    ToggleCrop,
    ZoomIn,
    ZoomOut,
    ResetZoom,
    BringForward,
    SendBackward,
    BringToFront,
    SendToBack
}

/// <summary>
/// A resolved key: the command and, for <see cref="CompositionCommand.Nudge"/>, the step in
/// canvas pixels on each axis.
/// </summary>
public readonly record struct CompositionKeyAction(CompositionCommand Command, int StepX = 0, int StepY = 0);

/// <summary>
/// The keyboard shortcuts of the composition canvas, written in one place: the key that
/// triggers a command is the one the menu and the button tooltips show. Combinations follow
/// OBS where it has them (Ctrl+D, Ctrl+F, Ctrl+R): an operator coming from OBS has nothing to
/// relearn.
/// </summary>
/// <remarks>
/// These keys are only heard on the preview, while it has the keyboard: a single letter
/// (R, C) therefore steals nothing from a text field.
/// </remarks>
public static class CompositionShortcuts
{
    // An arrow step, and a step with Shift: pixel precision, then enough to cross the canvas
    // without spending the day on it.
    public const int NudgeStep = 1;
    public const int LargeNudgeStep = 10;

    private sealed record Binding(CompositionCommand Command, Key Key, KeyModifiers Modifiers, string Keys);

    private static readonly Binding[] Bindings =
    [
        new(CompositionCommand.SelectNext, Key.Tab, KeyModifiers.None, "Tab"),
        new(CompositionCommand.SelectPrevious, Key.Tab, KeyModifiers.Shift, "Maj+Tab"),
        new(CompositionCommand.CenterSelected, Key.D, KeyModifiers.Control, "Ctrl+D"),
        new(CompositionCommand.FitSelected, Key.F, KeyModifiers.Control, "Ctrl+F"),
        new(CompositionCommand.ResetTransform, Key.R, KeyModifiers.Control, "Ctrl+R"),
        new(CompositionCommand.RotateRight, Key.R, KeyModifiers.None, "R"),
        new(CompositionCommand.RotateLeft, Key.R, KeyModifiers.Shift, "Maj+R"),
        new(CompositionCommand.ToggleCrop, Key.C, KeyModifiers.None, "C"),
        new(CompositionCommand.BringForward, Key.PageUp, KeyModifiers.None, "Page↑"),
        new(CompositionCommand.SendBackward, Key.PageDown, KeyModifiers.None, "Page↓"),
        new(CompositionCommand.BringToFront, Key.PageUp, KeyModifiers.Control, "Ctrl+Page↑"),
        new(CompositionCommand.SendToBack, Key.PageDown, KeyModifiers.Control, "Ctrl+Page↓")
    ];

    /// <summary>
    /// The command a key asks for, or nothing. Alt does not count: it holds the crop, and a
    /// command must answer the same whether cropping or not. For zoom, the typed character
    /// counts, not the key: "+" is not in the same place on every keyboard.
    /// </summary>
    public static CompositionKeyAction? Resolve(Key key, KeyModifiers modifiers, string? symbol = null)
    {
        var held = modifiers & ~KeyModifiers.Alt;

        if (key is Key.Left or Key.Right or Key.Up or Key.Down && (held & ~KeyModifiers.Shift) == 0)
        {
            var step = held.HasFlag(KeyModifiers.Shift) ? LargeNudgeStep : NudgeStep;
            return new CompositionKeyAction(
                CompositionCommand.Nudge,
                key switch { Key.Left => -step, Key.Right => step, _ => 0 },
                key switch { Key.Up => -step, Key.Down => step, _ => 0 });
        }

        foreach (var binding in Bindings)
        {
            if (binding.Key == key && binding.Modifiers == held) return new CompositionKeyAction(binding.Command);
        }

        // Zoom needs no modifier, but Shift may be required to type "+".
        if ((held & ~KeyModifiers.Shift) != 0) return null;

        if (key is Key.Add or Key.OemPlus || symbol == "+") return new CompositionKeyAction(CompositionCommand.ZoomIn);
        if (key is Key.Subtract or Key.OemMinus || symbol == "-") return new CompositionKeyAction(CompositionCommand.ZoomOut);
        if (key == Key.NumPad0 || symbol == "0") return new CompositionKeyAction(CompositionCommand.ResetZoom);

        return null;
    }

    /// <summary>A command's keys in plain text, as the menu and tooltips show them.</summary>
    public static string KeysOf(CompositionCommand command) => command switch
    {
        CompositionCommand.ZoomIn => "+",
        CompositionCommand.ZoomOut => "−",
        CompositionCommand.ResetZoom => "0",
        CompositionCommand.Nudge => "Flèches",
        _ => Bindings.First(binding => binding.Command == command).Keys
    };

    /// <summary>A button tooltip: what it does, then the key that does the same.</summary>
    public static string Describe(string label, CompositionCommand command) => $"{label} ({KeysOf(command)})";

    /// <summary>
    /// A command's keys one by one, to draw them as keycaps: "Ctrl+Page↑" gives "Ctrl",
    /// "Page↑". "+" stays a key of its own.
    /// </summary>
    public static IReadOnlyList<string> KeyTokensOf(CompositionCommand command)
    {
        var keys = KeysOf(command);
        return keys.Length > 1 ? keys.Split('+') : [keys];
    }
}
