using System.Collections.Generic;
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
    private readonly IFactory _factory;
    private readonly DockLayoutService _layoutService;
    private readonly List<IDockWindow> _pendingFloatingPanels = [];

    [ObservableProperty] private IRootDock? _layout;

    internal StudioDockViewModel(StudioViewModel studioViewModel, DockLayoutService layoutService)
    {
        _layoutService = layoutService;
        var factory = new StudioDockFactory(studioViewModel);
        _factory = factory;

        var layout = _layoutService.Load() as IRootDock ?? factory.CreateLayout();

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
    // window is up, so each one takes it as its owner and follows it when it is minimized.
    public void PresentFloatingPanels()
    {
        if (Layout is not { } root || _pendingFloatingPanels.Count == 0) return;

        var windows = _pendingFloatingPanels.ToArray();
        _pendingFloatingPanels.Clear();

        foreach (var window in windows)
        {
            _factory.AddWindow(root, window);
            window.Present(window.IsModal);
        }
    }

    public void SaveLayout()
    {
        if (Layout is not { } root) return;

        // Each detached panel's position and size is already in the model: Dock writes it back
        // whenever its window moves, resizes or closes. Reading it again here would be worse
        // than useless, because a window that has closed reports its position as 0,0.
        _layoutService.Save(root);
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
