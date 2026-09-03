using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;

namespace CastorApplication.Views.Shell;

public partial class StudioDockView : UserControl
{
    public StudioDockView()
    {
        InitializeComponent();

        // Dragging a panel out only detaches it where it is released outside the docking area,
        // and a maximized window leaves almost none: another screen, or the navigation bar.
        // Double-clicking a panel's bar detaches it wherever the window is.
        AddHandler(InputElement.DoubleTappedEvent, OnDoubleTapped, handledEventsToo: true);
    }

    private static void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Visual source)
        {
            return;
        }

        // The bar's own buttons (pin, menu, close) keep their single-click behaviour.
        if (source.FindAncestorOfType<Button>() is not null)
        {
            return;
        }

        // Only the panel's bar detaches, not its content: a double-click inside a panel belongs
        // to whatever it holds.
        var bar = (Visual?)source.FindAncestorOfType<ToolChromeControl>()
                  ?? source.FindAncestorOfType<DocumentTabStripItem>();

        if (bar?.DataContext is not IDockable dockable || dockable.Factory is not { } factory)
        {
            return;
        }

        // Refuses on its own when the panel cannot be detached.
        factory.FloatDockable(dockable);
        e.Handled = true;
    }
}
