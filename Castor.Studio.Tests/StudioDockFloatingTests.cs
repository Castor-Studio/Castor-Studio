using System;
using System.IO;
using System.Linq;
using CastorApplication.Docking;
using CastorApplication.Services.Settings;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Castor.Studio.Tests;

public sealed class StudioDockFloatingTests
{
    [Fact]
    public void Every_studio_panel_can_be_detached()
    {
        var (factory, root) = CreateStudioLayout();

        foreach (var id in new[] { StudioDockIds.Preview, StudioDockIds.SceneSelector, StudioDockIds.Status, StudioDockIds.StreamControls })
        {
            var panel = factory.FindDockable(root, dockable => dockable.Id == id);
            Assert.NotNull(panel);
            Assert.True(panel!.CanFloat, $"{id} cannot be detached.");
            Assert.True(((IDockable)panel.Owner!).CanFloat, $"The dock holding {id} cannot be detached.");
        }
    }

    [Fact]
    public void Detaching_a_panel_moves_it_into_a_window_of_its_own()
    {
        var (factory, root) = CreateStudioLayout();

        var status = factory.FindDockable(root, dockable => dockable.Id == StudioDockIds.Status)!;

        factory.FloatDockable(status);

        var window = Assert.Single(root.Windows!);
        Assert.True(window.Host!.IsTracked);
        // The window title bar reads this, so it has to name the panel, not its dock type.
        Assert.Equal("Statut", (window.Layout!.ActiveDockable as IDock)!.Title);
        // The panel now lives in the floating window layout, and the row it left closed up behind it.
        Assert.Same(window.Layout, factory.FindRoot(status));
        Assert.Equal(
            [StudioDockIds.SceneSelectorDock, null, StudioDockIds.StreamControlsDock],
            ChildIds(factory, root, StudioDockIds.BottomBar));
    }

    [Fact]
    public void Closing_a_floating_window_docks_its_panel_back_where_it_belongs()
    {
        var (factory, root) = CreateStudioLayout();
        factory.FloatDockable(factory.FindDockable(root, dockable => dockable.Id == StudioDockIds.Status)!);
        var window = root.Windows!.Single();

        Assert.True(factory.OnWindowClosing(window));

        Assert.NotNull(factory.FindDockable(root, dockable => dockable.Id == StudioDockIds.Status));
        Assert.Empty(window.Layout!.VisibleDockables!);
        Assert.Equal(
            [StudioDockIds.StatusDock, null, StudioDockIds.StreamControlsDock],
            ChildIds(factory, root, StudioDockIds.ControlsRow));
    }

    [Fact]
    public void A_docked_back_panel_keeps_the_order_of_the_default_layout()
    {
        var (factory, root) = CreateStudioLayout();
        // Detaching the whole dock is what dragging a single-panel chrome bar does.
        factory.FloatDockable(factory.FindDockable(root, dockable => dockable.Id == StudioDockIds.StreamControlsDock)!);

        factory.OnWindowClosing(root.Windows!.Single());

        Assert.Equal(
            [StudioDockIds.StatusDock, null, StudioDockIds.StreamControlsDock],
            ChildIds(factory, root, StudioDockIds.ControlsRow));
    }

    [Fact]
    public void Docking_back_rebuilds_a_row_that_emptied_while_both_its_panels_were_detached()
    {
        var (factory, root) = CreateStudioLayout();
        factory.FloatDockable(factory.FindDockable(root, dockable => dockable.Id == StudioDockIds.Status)!);
        factory.FloatDockable(factory.FindDockable(root, dockable => dockable.Id == StudioDockIds.StreamControls)!);
        Assert.Null(factory.FindDockable(root, dockable => dockable.Id == StudioDockIds.ControlsRow));

        foreach (var window in root.Windows!.ToList())
        {
            factory.OnWindowClosing(window);
        }

        Assert.Equal(
            [StudioDockIds.StatusDock, null, StudioDockIds.StreamControlsDock],
            ChildIds(factory, root, StudioDockIds.ControlsRow));
        Assert.Equal(
            [StudioDockIds.SceneSelectorDock, null, StudioDockIds.ControlsRow],
            ChildIds(factory, root, StudioDockIds.BottomBar));
    }

    [Fact]
    public void Docking_back_restores_the_sizes_of_the_default_layout()
    {
        var (factory, root) = CreateStudioLayout();
        factory.FloatDockable(factory.FindDockable(root, dockable => dockable.Id == StudioDockIds.Status)!);

        factory.OnWindowClosing(root.Windows!.Single());

        // Collapsing the row had handed its own proportion to the panel left behind.
        Assert.Equal(0.75, Proportion(factory, root, StudioDockIds.StatusDock));
        Assert.Equal(0.25, Proportion(factory, root, StudioDockIds.StreamControlsDock));
    }

    [Fact]
    public void A_detached_panel_keeps_its_place_and_size_across_a_restart()
    {
        var layoutFile = Path.Combine(Path.GetTempPath(), $"castor-dock-layout-{Guid.NewGuid():N}.json");
        var service = new DockLayoutService(layoutFile);

        try
        {
            var (factory, root) = CreateStudioLayout();
            factory.FloatDockable(factory.FindDockable(root, dockable => dockable.Id == StudioDockIds.Status)!);

            var host = (FakeHostWindow)root.Windows!.Single().Host!;
            host.SetPosition(320, 240);
            host.SetSize(640, 480);
            // Stands in for Dock writing the geometry back as the user moves the window.
            root.Windows!.Single().Save();
            service.Save(root);

            var reloaded = Assert.IsAssignableFrom<IRootDock>(service.Load());
            var window = Assert.Single(reloaded.Windows!);

            Assert.Equal(320, window.X);
            Assert.Equal(240, window.Y);
            Assert.Equal(640, window.Width);
            Assert.Equal(480, window.Height);
            Assert.NotNull(window.Layout);
            Assert.IsType<StatusTool>(
                new StudioDockFactory(null).FindDockable(window.Layout!, dockable => dockable.Id == StudioDockIds.Status));
        }
        finally
        {
            File.Delete(layoutFile);
        }
    }

    private static (StudioDockFactory Factory, IRootDock Root) CreateStudioLayout()
    {
        var factory = new StudioDockFactory(paneContext: null, () => new FakeHostWindow());
        var root = factory.CreateLayout();
        factory.InitLayout(root);
        return (factory, root);
    }

    private static double Proportion(StudioDockFactory factory, IRootDock root, string dockId)
        => Assert.IsAssignableFrom<IDock>(factory.FindDockable(root, dockable => dockable.Id == dockId)).Proportion;

    // Ids of a dock's children, with null standing in for the splitters between them.
    private static string?[] ChildIds(StudioDockFactory factory, IRootDock root, string dockId)
    {
        var dock = Assert.IsAssignableFrom<IDock>(factory.FindDockable(root, dockable => dockable.Id == dockId));
        return dock.VisibleDockables!
            .Select(dockable => dockable is IProportionalDockSplitter ? null : dockable.Id)
            .ToArray();
    }

    private sealed class FakeHostWindow : IHostWindow
    {
        private double _x;
        private double _y;
        private double _width;
        private double _height;
        private DockWindowState _windowState = DockWindowState.Normal;

        public IHostWindowState? HostWindowState => null;

        public bool IsTracked { get; set; }

        public IDockWindow? Window { get; set; }

        public void Present(bool isDialog) { }

        public void Exit() { }

        public void SetPosition(double x, double y) => (_x, _y) = (x, y);

        public void GetPosition(out double x, out double y) => (x, y) = (_x, _y);

        public void SetSize(double width, double height) => (_width, _height) = (width, height);

        public void GetSize(out double width, out double height) => (width, height) = (_width, _height);

        public void SetWindowState(DockWindowState windowState) => _windowState = windowState;

        public DockWindowState GetWindowState() => _windowState;

        public void SetTitle(string? title) { }

        public void SetLayout(IDock layout) { }

        public void SetActive() { }
    }
}
