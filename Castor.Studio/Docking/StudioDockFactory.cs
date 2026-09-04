using System;
using System.Collections.Generic;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;

namespace CastorApplication.Docking;

// paneContext is whatever the panes bind to - StudioViewModel in the app. The factory only
// hands it back through ContextLocator, so it stays untyped and tests can pass their own.
public sealed partial class StudioDockFactory(object? paneContext, Func<IHostWindow?>? hostWindowFactory = null) : Factory
{
    // A detached panel lives in a platform window of its own. DockControl only installs a
    // default host window locator when InitializeFactory is on, and that would also overwrite
    // the ContextLocator below, so the factory brings its own.
    private readonly Func<IHostWindow?> _hostWindowFactory = hostWindowFactory ?? (() => new StudioHostWindow());

    public override IRootDock CreateLayout()
    {
        // CanFloat lets a panel be dragged out of the main window; CanClose stays off because
        // the Studio page needs all four of them (see OnWindowClosing for what a closed
        // floating window does with the panels it was holding).
        var preview = new PreviewDocument { Id = StudioDockIds.Preview, Title = "Aperçu", CanClose = false, CanFloat = true };
        var sceneSelector = new SceneSelectorTool { Id = StudioDockIds.SceneSelector, Title = "Scène active", CanClose = false, CanFloat = true };
        var status = new StatusTool { Id = StudioDockIds.Status, Title = "Statut", CanClose = false, CanFloat = true };
        var streamControls = new StreamControlsTool { Id = StudioDockIds.StreamControls, Title = "Diffusion & Enregistrement", CanClose = false, CanFloat = true };

        // Real screen-relative minimums are applied once the window is shown (see
        // StudioDockViewModel.ApplyScreenSize) - this is just a sane floor until then.
        foreach (var dockable in new IDockable[] { preview, sceneSelector, status, streamControls })
        {
            var minSize = StudioDockSizing.GetMinSize(dockable.Id!, StudioDockSizing.FallbackScreenSize);
            dockable.MinWidth = minSize.Width;
            dockable.MinHeight = minSize.Height;
        }

        // The docks float too: with a single pane inside, the chrome bar - not the hidden tab
        // strip - is the drag handle, and dragging it detaches the dock rather than the pane.
        var previewDock = new DocumentDock
        {
            Id = StudioDockIds.PreviewDock,
            Title = "Aperçu",
            ActiveDockable = preview,
            VisibleDockables = CreateList<IDockable>(preview),
            CanCreateDocument = false,
            CanClose = false,
            CanFloat = true,
            Proportion = 0.82,
        };

        var sceneSelectorDock = new ToolDock
        {
            Id = StudioDockIds.SceneSelectorDock,
            Title = "Scène active",
            ActiveDockable = sceneSelector,
            VisibleDockables = CreateList<IDockable>(sceneSelector),
            CanClose = false,
            CanFloat = true,
            Proportion = 0.35,
        };

        var statusDock = new ToolDock
        {
            Id = StudioDockIds.StatusDock,
            Title = "Statut",
            ActiveDockable = status,
            VisibleDockables = CreateList<IDockable>(status),
            CanClose = false,
            CanFloat = true,
            Proportion = 0.75,
        };

        var streamControlsDock = new ToolDock
        {
            Id = StudioDockIds.StreamControlsDock,
            Title = "Diffusion & Enregistrement",
            ActiveDockable = streamControls,
            VisibleDockables = CreateList<IDockable>(streamControls),
            CanClose = false,
            CanFloat = true,
            Proportion = 0.25,
        };

        var controlsRow = new ProportionalDock
        {
            Id = StudioDockIds.ControlsRow,
            Orientation = Orientation.Horizontal,
            Proportion = 0.65,
            VisibleDockables = CreateList<IDockable>(statusDock, new ProportionalDockSplitter(), streamControlsDock),
        };

        var bottomBar = new ProportionalDock
        {
            Id = StudioDockIds.BottomBar,
            Orientation = Orientation.Vertical,
            Proportion = 0.18,
            VisibleDockables = CreateList<IDockable>(sceneSelectorDock, new ProportionalDockSplitter(), controlsRow),
        };

        var studioColumn = new ProportionalDock
        {
            Id = StudioDockIds.StudioColumn,
            Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>(previewDock, new ProportionalDockSplitter(), bottomBar),
        };

        var root = CreateRootDock();
        root.Id = StudioDockIds.Root;
        root.ActiveDockable = studioColumn;
        root.DefaultDockable = studioColumn;
        root.VisibleDockables = CreateList<IDockable>(studioColumn);
        root.CanFloat = false;

        return root;
    }

    public override void InitLayout(IDockable layout)
    {
        ContextLocator = new Dictionary<string, Func<object?>>
        {
            [StudioDockIds.Preview] = () => paneContext,
            [StudioDockIds.SceneSelector] = () => paneContext,
            [StudioDockIds.Status] = () => paneContext,
            [StudioDockIds.StreamControls] = () => paneContext,
        };

        DefaultHostWindowLocator = _hostWindowFactory;
        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = _hostWindowFactory,
        };

        base.InitLayout(layout);
    }

    public override IDockWindow? CreateWindowFrom(IDockable dockable)
    {
        var window = base.CreateWindowFrom(dockable);

        // Detaching a single panel wraps it in a new dock that Dock names after its interface,
        // and that name is what the window title bar ends up showing. Name it after the panel.
        if (window?.Layout?.ActiveDockable is IDock created && created.ActiveDockable is { } panel)
        {
            created.Title = panel.Title;
        }

        return window;
    }

    public override bool OnWindowClosing(IDockWindow? window)
    {
        // Panels cannot be closed, so a floating window that the user dismisses has to hand
        // them back rather than take them down with it. IsTracked tells the two cases apart:
        // HostAdapter.Exit clears it before every programmatic teardown (ExitWindows when the
        // app closes, RemoveWindow once a panel has been docked back), while a window closed
        // from its own chrome or by the OS still has it set.
        if (window is { Host.IsTracked: true })
        {
            ReturnFloatingDockablesHome(window);
        }

        return base.OnWindowClosing(window);
    }
}
