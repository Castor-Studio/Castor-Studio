using System.Collections.Generic;
using System.Linq;
using Avalonia;
using CastorApplication.Docking;
using CastorApplication.Services.Settings;
using CastorApplication.ViewModels.Studio;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace CastorApplication.ViewModels.Shell;

public partial class StudioDockViewModel : ViewModelBase
{
    private readonly StudioDockFactory _factory;
    private readonly DockLayoutService _layoutService;
    private readonly List<IDockWindow> _pendingFloatingPanels = [];

    // Every panel of the workspace, for the menu bar to offer them.
    public IReadOnlyList<StudioPanel> Panels { get; }

    [ObservableProperty] private IRootDock? _layout;

    internal StudioDockViewModel(StudioViewModel studioViewModel, DockLayoutService layoutService)
    {
        _layoutService = layoutService;
        _factory = new StudioDockFactory(studioViewModel);
        Panels = _factory.Panels();

        IRootDock layout;
        if (_layoutService.Load() is IRootDock saved)
        {
            layout = saved;

            // The saved arrangement is kept, but what each panel is and is allowed to do comes
            // from the factory: a layout written by an older build would otherwise keep panels
            // that cannot be detached, and docks with no name.
            _factory.ApplyPanelDefaults(layout);
        }
        else
        {
            layout = _factory.CreateLayout();
        }

        // Panels that were detached when the app last closed are held back until the main
        // window exists (see PresentFloatingPanels): InitLayout would otherwise put them on
        // screen first, with no window for them to belong to.
        if (layout.Windows is { Count: > 0 } restored)
        {
            _pendingFloatingPanels.AddRange(restored);
            layout.Windows = null;
        }

        _factory.InitLayout(layout);
        Layout = layout;
    }

    // Reopens the panels that were detached when the app last closed. Called once the main
    // window is up, so each one is opened on a desktop that already has something on it.
    public void PresentFloatingPanels()
    {
        if (Layout is not { } root) return;

        foreach (var window in TakePendingPanels(root))
        {
            window.Present(window.IsModal);
        }
    }

    // Puts a panel back on screen, docking it home when it is nowhere to be found. Panels cannot
    // be closed, so this is a way out of a layout that lost one.
    public void ShowPanel(string id)
    {
        if (Layout is { } root) _factory.ShowPanel(root, id);
    }

    // Throws the arrangement away and starts from the one the app ships with, detached panels
    // included. The last resort when a workspace has been rearranged into something unusable.
    public void ResetLayout()
    {
        if (Layout is { Windows: { } windows })
        {
            foreach (var window in windows.ToList())
            {
                window.Exit();
            }
        }

        _pendingFloatingPanels.Clear();

        var layout = _factory.CreateLayout();
        _factory.InitLayout(layout);
        Layout = layout;
    }

    public void SaveLayout()
    {
        if (Layout is not { } root) return;

        // Panels held back from the last launch that were never presented still belong to the
        // layout; leaving them out here would lose them for good.
        TakePendingPanels(root);

        // Each detached panel's position and size is already in the model: Dock writes it back
        // whenever its window moves, resizes or closes. Reading it again here would be worse
        // than useless, because a window that has closed reports its position as 0,0.
        _layoutService.Save(root);
    }

    // Hands the held-back panels to the layout, so that whatever happens next they are part of
    // what gets saved.
    private List<IDockWindow> TakePendingPanels(IRootDock root)
    {
        if (_pendingFloatingPanels.Count == 0) return [];

        var pending = new List<IDockWindow>(_pendingFloatingPanels);
        _pendingFloatingPanels.Clear();

        foreach (var window in pending)
        {
            _factory.AddWindow(root, window);
        }

        return pending;
    }

    // Called once the window is shown on a real screen, so pane minimums scale with the
    // display instead of being stuck at the design-time fallback used at CreateLayout time.
    public void ApplyScreenSize(Size screenSize)
    {
        if (Layout is not { } root) return;

        ApplyScreenSize(root, screenSize);

        if (root.Windows is { } windows)
        {
            foreach (var window in windows)
            {
                if (window.Layout is { } floating) ApplyScreenSize(floating, screenSize);
            }
        }
    }

    private void ApplyScreenSize(IDock scope, Size screenSize)
    {
        foreach (var id in new[] { StudioDockIds.Preview, StudioDockIds.SceneSelector, StudioDockIds.Status, StudioDockIds.StreamControls })
        {
            if (_factory.FindDockable(scope, d => d.Id == id) is not { } dockable) continue;
            var minSize = StudioDockSizing.GetMinSize(id, screenSize);
            dockable.MinWidth = minSize.Width;
            dockable.MinHeight = minSize.Height;
        }
    }
}
