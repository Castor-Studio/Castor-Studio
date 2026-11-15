using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Core;

namespace CastorApplication.Docking;

// A window that can fill its screen. StudioHostWindow in the app; tests bring their own.
public interface IFullscreenHost
{
    void ToggleFullscreen();
}

// Fullscreen belongs to the detached preview: pulled out onto another screen, its window can
// fill that screen as a program monitor. Docked in the main window it has nowhere to go
// fullscreen without covering the app, so the command is not offered there.
public sealed partial class StudioDockFactory
{
    // Bound by the panel bar button (StudioPanelChrome) and menu (Styles/DockMenus.axaml), both
    // with the dock the bar belongs to. Refuses anything but a detached window holding the preview.
    public ICommand TogglePreviewFullscreenCommand =>
        _togglePreviewFullscreenCommand ??= new RelayCommand<IDockable?>(
            dockable => PreviewFullscreenHostOf(dockable)?.ToggleFullscreen(),
            dockable => PreviewFullscreenHostOf(dockable) is not null);

    private ICommand? _togglePreviewFullscreenCommand;

    // A detached window gets its host once it is shown, which can be after its bar asked the
    // command; the window calls this when it opens so the bar button and menu ask again.
    public void RefreshPreviewFullscreenAvailability() =>
        (_togglePreviewFullscreenCommand as IRelayCommand)?.NotifyCanExecuteChanged();

    public IFullscreenHost? PreviewFullscreenHostOf(IDockable? dockable)
    {
        var holdsPreview = dockable is PreviewTool
                           || dockable is IDock dock && FindDockable(dock, child => child is PreviewTool) is not null;

        return holdsPreview ? DetachedWindowOf(dockable)?.Host as IFullscreenHost : null;
    }
}
