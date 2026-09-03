using Avalonia.Controls;
using Avalonia.Input;
using CastorApplication.Docking;

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
        if (StudioPanelBar.Find(e.Source) is not { } dockable || dockable.Factory is not { } factory)
        {
            return;
        }

        // Refuses on its own when the panel cannot be detached.
        factory.FloatDockable(dockable);
        e.Handled = true;
    }
}
