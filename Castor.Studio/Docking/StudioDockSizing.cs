using System.Collections.Generic;
using Avalonia;

namespace CastorApplication.Docking;

public static class StudioDockSizing
{
    // Used before the real screen size is known (matches MainWindow's design-time fallback).
    public static readonly Size FallbackScreenSize = new(1280, 800);

    private static readonly Dictionary<string, (double Width, double Height)> Fractions = new()
    {
        [StudioDockIds.Preview] = (0.25, 0.25),
        [StudioDockIds.SceneSelector] = (0.18, 0.035),
        [StudioDockIds.Status] = (0.16, 0.05),
        [StudioDockIds.StreamControls] = (0.14, 0.05),
    };

    public static Size GetMinSize(string paneId, Size screenSize)
    {
        var (widthFraction, heightFraction) = Fractions.GetValueOrDefault(paneId, (0.1, 0.03));
        return new Size(screenSize.Width * widthFraction, screenSize.Height * heightFraction);
    }
}
