namespace CastorApplication.ViewModels.Scenes;

// 24x24 stroke icons (Tabler style) for the source menu on the composition canvas, drawn like
// SourceIcons: a 2 px round stroke.
public static class CompositionIcons
{
    public const string Crop = "M8 5v10a1 1 0 0 0 1 1h10 M5 8h10a1 1 0 0 1 1 1v10";
    public const string RotateRight = "M4.05 11a8 8 0 1 1 .5 4 M4.05 20v-5h5";
    public const string RotateLeft = "M19.95 11a8 8 0 1 0 -.5 4 M19.95 20v-5h-5";
    public const string RotateHalf = "M12 4a8 8 0 1 1 -8 8 M4 8v4h4";
    public const string Restore = "M3 12a9 9 0 1 0 9 -9a9.75 9.75 0 0 0 -6.74 2.74l-2.26 2.26 M3 3v5h5";
    public const string Center = "M11.5 12a.5 .5 0 1 0 1 0a.5 .5 0 1 0 -1 0 M4 8v-2a2 2 0 0 1 2 -2h2 M4 16v2a2 2 0 0 0 2 2h2 M16 4h2a2 2 0 0 1 2 2v2 M16 20h2a2 2 0 0 0 2 -2v-2";
    public const string Fit = "M16 4h4v4 M14 10l6 -6 M8 20h-4v-4 M4 20l6 -6 M16 20h4v-4 M14 14l6 6 M8 4h-4v4 M4 4l6 6";
    public const string ToFront = "M12 10v10 M12 10l4 4 M12 10l-4 4 M4 4h16";
    public const string Forward = "M6 15l6 -6l6 6";
    public const string Backward = "M6 9l6 6l6 -6";
    public const string ToBack = "M4 20h16 M12 14v-10 M12 14l4 -4 M12 14l-4 -4";
    public const string Zoom = "M3 10a7 7 0 1 0 14 0a7 7 0 1 0 -14 0 M7 10h6 M10 7v6 M21 21l-6 -6";
    // What can be done from the source list.
    public const string Rename = "M4 20h4l10.5 -10.5a2.828 2.828 0 1 0 -4 -4l-10.5 10.5v4 M13.5 6.5l4 4";
    public const string Loop = "M4 12v-3a3 3 0 0 1 3 -3h13 M17 3l3 3l-3 3 M20 12v3a3 3 0 0 1 -3 3h-13 M7 21l-3 -3l3 -3";
    public const string Remove = "M4 7h16 M10 11v6 M14 11v6 M5 7l1 12a2 2 0 0 0 2 2h8a2 2 0 0 0 2 -2l1 -12 M9 7v-3a1 1 0 0 1 1 -1h4a1 1 0 0 1 1 1v3";

    /// <summary>
    /// The icon of a command, the same in the source menu and in the canvas toolbar: a command
    /// is recognizable from one place to the other.
    /// </summary>
    public static string Of(CompositionCommand command) => command switch
    {
        CompositionCommand.ToggleCrop => Crop,
        CompositionCommand.RotateRight => RotateRight,
        CompositionCommand.RotateLeft => RotateLeft,
        CompositionCommand.CenterSelected => Center,
        CompositionCommand.FitSelected => Fit,
        CompositionCommand.BringToFront => ToFront,
        CompositionCommand.BringForward => Forward,
        CompositionCommand.SendBackward => Backward,
        CompositionCommand.SendToBack => ToBack,
        CompositionCommand.ZoomIn or CompositionCommand.ZoomOut => Zoom,
        _ => Restore
    };
}
