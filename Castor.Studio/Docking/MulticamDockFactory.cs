using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;

namespace CastorApplication.Docking;

/// <summary>
/// The multicam workspace: one document per scene, side by side, each able to be
/// dragged out into a window of its own.
/// </summary>
/// <remarks>
/// Unlike the Studio page, this layout is not persisted. It is derived from the scenes
/// that exist right now, so there is no stale arrangement to reconcile against a scene
/// set that changed between two launches - the case that makes dynamic docking hard.
/// </remarks>
public sealed class MulticamDockFactory(Func<IHostWindow?>? hostWindowFactory = null) : Factory
{
    public const string DocumentDockId = "MulticamDocuments";
    public const string RootId = "MulticamRoot";
    public const string DisplayId = "MulticamDisplay";
    public const string AiBarId = "MulticamAiBar";
    public const string AiDockId = "MulticamAiDock";

    private readonly Func<IHostWindow?> _hostWindowFactory = hostWindowFactory ?? (() => new StudioHostWindow());

    // Dock resolves every dockable's Context through this locator while initializing the
    // layout, overwriting whatever was set at construction - so the tiles have to be
    // reachable by id here, not just carried on the documents.
    private Dictionary<string, Func<object?>> _contexts = new();

    public override IRootDock CreateLayout() => CreateDisplayLayout(new object());

    /// <summary>The whole display as a single, floatable panel.</summary>
    public IRootDock CreateDisplayLayout(object context)
    {
        _contexts = new Dictionary<string, Func<object?>>
        {
            [DisplayId] = () => context,
            [AiBarId] = () => context,
        };

        var display = new MulticamDisplayTool
        {
            Id = DisplayId,
            Title = "Aperçu",
            Context = context,
            CanFloat = true,
            CanClose = false,
        };

        var displayDock = new ToolDock
        {
            Id = DocumentDockId,
            Title = "Aperçu",
            VisibleDockables = CreateList<IDockable>(display),
            ActiveDockable = display,
            CanClose = false,
            CanFloat = true,
        };

        var aiBar = new MulticamAiTool
        {
            Id = AiBarId,
            Title = "IA",
            Context = context,
            CanFloat = true,
            CanClose = false,
        };

        var aiDock = new ToolDock
        {
            Id = AiDockId,
            Title = "IA",
            VisibleDockables = CreateList<IDockable>(aiBar),
            ActiveDockable = aiBar,
            CanClose = false,
            CanFloat = true,
            Proportion = 0.10,
        };

        // The controls sit above the display, but as a panel: an operator who wants them
        // elsewhere - or on another screen - can move them.
        var column = new ProportionalDock
        {
            Id = "MulticamColumn",
            Orientation = Dock.Model.Core.Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>(aiDock, new ProportionalDockSplitter(), displayDock),
        };

        return WrapInRoot(column);
    }

    private IRootDock WrapInRoot(IDock content)
    {
        var root = CreateRootDock();
        root.Id = RootId;
        root.ActiveDockable = content;
        root.DefaultDockable = content;
        root.VisibleDockables = CreateList<IDockable>(content);
        root.CanFloat = false;
        return root;
    }

    public override void InitLayout(IDockable layout)
    {
        ContextLocator = _contexts;

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

        // Detaching wraps the document in a dock that Dock names after its interface, and
        // that name is what the window title bar shows. Name it after the scene instead.
        if (window?.Layout?.ActiveDockable is IDock created && created.ActiveDockable is { } panel)
        {
            created.Title = panel.Title;
        }

        return window;
    }
}
