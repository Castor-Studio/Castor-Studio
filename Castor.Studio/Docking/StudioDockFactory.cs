using System;
using System.Collections.Generic;
using CastorApplication.ViewModels.Studio;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;

namespace CastorApplication.Docking;

public sealed class StudioDockFactory(StudioViewModel studioViewModel) : Factory
{
    public override IRootDock CreateLayout()
    {
        var preview = new PreviewDocument { Id = StudioDockIds.Preview, Title = "Aperçu", CanClose = false, CanFloat = false };
        var sceneSelector = new SceneSelectorTool { Id = StudioDockIds.SceneSelector, Title = "Scène active", CanClose = false, CanFloat = false };
        var status = new StatusTool { Id = StudioDockIds.Status, Title = "Statut", CanClose = false, CanFloat = false };
        var streamControls = new StreamControlsTool { Id = StudioDockIds.StreamControls, Title = "Diffusion & Enregistrement", CanClose = false, CanFloat = false };

        var previewDock = new DocumentDock
        {
            Id = StudioDockIds.PreviewDock,
            ActiveDockable = preview,
            VisibleDockables = CreateList<IDockable>(preview),
            CanCreateDocument = false,
            CanClose = false,
            CanFloat = false,
            Proportion = 0.82,
        };

        var sceneSelectorDock = new ToolDock
        {
            Id = StudioDockIds.SceneSelectorDock,
            ActiveDockable = sceneSelector,
            VisibleDockables = CreateList<IDockable>(sceneSelector),
            CanClose = false,
            CanFloat = false,
            Proportion = 0.35,
        };

        var statusDock = new ToolDock
        {
            Id = StudioDockIds.StatusDock,
            ActiveDockable = status,
            VisibleDockables = CreateList<IDockable>(status),
            CanClose = false,
            CanFloat = false,
            Proportion = 0.75,
        };

        var streamControlsDock = new ToolDock
        {
            Id = StudioDockIds.StreamControlsDock,
            ActiveDockable = streamControls,
            VisibleDockables = CreateList<IDockable>(streamControls),
            CanClose = false,
            CanFloat = false,
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
            [StudioDockIds.Preview] = () => studioViewModel,
            [StudioDockIds.SceneSelector] = () => studioViewModel,
            [StudioDockIds.Status] = () => studioViewModel,
            [StudioDockIds.StreamControls] = () => studioViewModel,
        };

        base.InitLayout(layout);
    }
}
