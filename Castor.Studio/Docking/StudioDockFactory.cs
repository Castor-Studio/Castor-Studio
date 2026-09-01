using System;
using System.Collections.Generic;
using CastorApplication.ViewModels.Multicam;
using CastorApplication.ViewModels.Scenes;
using CastorApplication.ViewModels.Studio;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;

namespace CastorApplication.Docking;

public sealed class StudioDockFactory(
    StudioViewModel studioViewModel,
    ScenesViewModel scenesViewModel,
    MulticamViewModel multicamViewModel) : Factory
{
    public override IRootDock CreateLayout()
    {
        var preview = new PreviewDocument { Id = StudioDockIds.Preview, Title = "Aperçu", CanClose = false, CanFloat = false };
        var sceneSelector = new SceneSelectorTool { Id = StudioDockIds.SceneSelector, Title = "Scène active", CanClose = false, CanFloat = false };
        var controls = new ControlsTool { Id = StudioDockIds.Controls, Title = "Contrôles", CanClose = false, CanFloat = false };
        var scenes = new Tool { Id = StudioDockIds.Scenes, Title = "Scènes", CanClose = false, CanFloat = false };
        var multicam = new Tool { Id = StudioDockIds.Multicam, Title = "Multi-caméras", CanClose = false, CanFloat = false };

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

        var controlsDock = new ToolDock
        {
            Id = StudioDockIds.ControlsDock,
            ActiveDockable = controls,
            VisibleDockables = CreateList<IDockable>(controls),
            CanClose = false,
            CanFloat = false,
            Proportion = 0.65,
        };

        var bottomBar = new ProportionalDock
        {
            Id = StudioDockIds.BottomBar,
            Orientation = Orientation.Vertical,
            Proportion = 0.18,
            VisibleDockables = CreateList<IDockable>(sceneSelectorDock, new ProportionalDockSplitter(), controlsDock),
        };

        var studioColumn = new ProportionalDock
        {
            Id = StudioDockIds.StudioColumn,
            Orientation = Orientation.Vertical,
            Proportion = 0.7,
            VisibleDockables = CreateList<IDockable>(previewDock, new ProportionalDockSplitter(), bottomBar),
        };

        var sidePanel = new ToolDock
        {
            Id = StudioDockIds.SidePanel,
            ActiveDockable = scenes,
            VisibleDockables = CreateList<IDockable>(scenes, multicam),
            CanClose = false,
            CanFloat = false,
            Proportion = 0.3,
        };

        var mainLayout = new ProportionalDock
        {
            Id = StudioDockIds.MainLayout,
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>(studioColumn, new ProportionalDockSplitter(), sidePanel),
        };

        var root = CreateRootDock();
        root.Id = StudioDockIds.Root;
        root.ActiveDockable = mainLayout;
        root.DefaultDockable = mainLayout;
        root.VisibleDockables = CreateList<IDockable>(mainLayout);
        root.CanFloat = false;

        return root;
    }

    public override void InitLayout(IDockable layout)
    {
        ContextLocator = new Dictionary<string, Func<object?>>
        {
            [StudioDockIds.Preview] = () => studioViewModel,
            [StudioDockIds.SceneSelector] = () => studioViewModel,
            [StudioDockIds.Controls] = () => studioViewModel,
            [StudioDockIds.Scenes] = () => scenesViewModel,
            [StudioDockIds.Multicam] = () => multicamViewModel,
        };

        base.InitLayout(layout);
    }
}
