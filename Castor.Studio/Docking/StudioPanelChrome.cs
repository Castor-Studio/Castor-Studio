using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Dock.Avalonia.Controls;
using Path = Avalonia.Controls.Shapes.Path;

namespace CastorApplication.Docking;

// Adds the fullscreen button to the bar Dock draws for a tool panel, next to its menu and pin
// buttons. Dock's bar has no slot for a button of our own and re-templating it would mean copying
// Dock's whole template, so the button is put into the row of buttons once Dock has built it.
// Every bar gets one; it only shows on a detached preview (StudioDockFactory.Fullscreen).
internal static class StudioPanelChrome
{
    private const string FullscreenIcon =
        "M4 8v-2a2 2 0 0 1 2 -2h2 M4 16v2a2 2 0 0 0 2 2h2 M16 4h2a2 2 0 0 1 2 2v2 M16 20h2a2 2 0 0 0 2 -2v-2";

    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<ToolChromeControl>(OnTemplateApplied);
    }

    private static void OnTemplateApplied(ToolChromeControl chrome, TemplateAppliedEventArgs e)
    {
        if (e.NameScope.Find<Button>("PART_MenuButton") is not { Parent: Panel buttons } menuButton) return;

        var icon = new Path
        {
            Data = Geometry.Parse(FullscreenIcon),
            Width = 10,
            Height = 10,
            Stretch = Stretch.Uniform,
            StrokeThickness = 2,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
        };
        // Follows the theme, like the icons Dock draws in the same bar.
        icon.Bind(Shape.StrokeProperty, icon.GetResourceObservable("DockChromeButtonForegroundBrush"));

        var fullscreen = new Button
        {
            // Dock's own look for its bar buttons, so size and hover match the menu button.
            Theme = chrome.PinButtonTheme ?? chrome.MenuButtonTheme,
            Content = icon,
            [ToolTip.TipProperty] = "Plein écran (F11)",
            // The bar's data context is the dock it belongs to, which is what the command takes.
            [!Button.CommandProperty] = new ReflectionBinding("Owner.Factory.TogglePreviewFullscreenCommand"),
            [!Button.CommandParameterProperty] = new ReflectionBinding("."),
        };
        // The command refuses every bar but a detached preview's, and the button only shows where it can act.
        fullscreen.Bind(Visual.IsVisibleProperty, fullscreen.GetObservable(InputElement.IsEffectivelyEnabledProperty));

        buttons.Children.Insert(buttons.Children.IndexOf(menuButton), fullscreen);
    }
}
