using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;

namespace CastorApplication.Docking;

// The window a detached panel lives in. It carries no system frame, so sending the panel back -
// which is what closing the window does - has no title bar button to hang off: double-clicking
// the panel's bar does it instead, the gesture that pulled the panel out in the first place.
// The panel's menu carries the same command, see Styles/DockMenus.axaml.
//
// Holding the preview, the window can also fill its screen (StudioDockFactory.Fullscreen): the
// bar button, F11 or a double-click on the picture enter it, Escape, F11 or a double-click leave.
public sealed class StudioHostWindow : HostWindow, IFullscreenHost
{
    private WindowState _stateBeforeFullscreen = WindowState.Normal;

    public StudioHostWindow()
    {
        AddHandler(DoubleTappedEvent, OnDoubleTapped, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnKeyDown, handledEventsToo: true);
    }

    private bool IsFullscreen => WindowState == WindowState.FullScreen;

    public void ToggleFullscreen()
    {
        if (IsFullscreen)
        {
            WindowState = _stateBeforeFullscreen;
        }
        else
        {
            _stateBeforeFullscreen = WindowState;
            WindowState = WindowState.FullScreen;
        }
    }

    protected override void OnOpened(System.EventArgs e)
    {
        base.OnOpened(e);
        (Window?.Factory as StudioDockFactory)?.RefreshPreviewFullscreenAvailability();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Styles/DockTheme.axaml drops the panel bar and the card border off this class, leaving
        // the bare picture. Also follows a fullscreen left by the system rather than by us.
        if (change.Property == WindowStateProperty)
        {
            Classes.Set("fullscreen", IsFullscreen);
        }
    }

    // A window closed in fullscreen would otherwise be saved at the size of the screen.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (IsFullscreen) WindowState = _stateBeforeFullscreen;
        base.OnClosing(e);
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (StudioPanelBar.Find(e.Source) is not null)
        {
            // Closing hands the panels back to the main window (StudioDockFactory.OnWindowClosing).
            Close();
            e.Handled = true;
            return;
        }

        if (HoldsPreview && IsOnPreview(e.Source))
        {
            ToggleFullscreen();
            e.Handled = true;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11 && HoldsPreview || e.Key == Key.Escape && IsFullscreen)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
    }

    private bool HoldsPreview => this.GetVisualDescendants().Any(visual => visual is StyledElement { DataContext: PreviewTool });

    // The picture is presented inside the element whose data context is the preview panel itself.
    private static bool IsOnPreview(object? source) =>
        source is Visual visual && visual.GetSelfAndVisualAncestors().Any(v => v is StyledElement { DataContext: PreviewTool });
}
