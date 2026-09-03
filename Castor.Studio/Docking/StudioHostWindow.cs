using Avalonia.Input;
using Dock.Avalonia.Controls;

namespace CastorApplication.Docking;

// The window a detached panel lives in. It carries no system frame, so sending the panel back -
// which is what closing the window does - has no title bar button to hang off: double-clicking
// the panel's bar does it instead, the gesture that pulled the panel out in the first place.
// The panel's menu carries the same command, see Styles/DockMenus.axaml.
public sealed class StudioHostWindow : HostWindow
{
    public StudioHostWindow()
    {
        AddHandler(DoubleTappedEvent, OnDoubleTapped, handledEventsToo: true);
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (StudioPanelBar.Find(e.Source) is null)
        {
            return;
        }

        // Closing hands the panels back to the main window (StudioDockFactory.OnWindowClosing).
        Close();
        e.Handled = true;
    }
}
