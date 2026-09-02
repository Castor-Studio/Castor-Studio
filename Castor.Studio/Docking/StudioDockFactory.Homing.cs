using System;
using System.Collections.Generic;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace CastorApplication.Docking;

// Sending a detached panel back where it belongs. "Where it belongs" is read from a pristine
// copy of the default layout, so CreateLayout stays the single description of the workspace.
public sealed partial class StudioDockFactory
{
    private readonly record struct HomeSlot(string ParentId, int Order);

    private IRootDock? _template;
    private Dictionary<string, HomeSlot>? _homes;

    private IRootDock Template => _template ??= CreateLayout();

    private Dictionary<string, HomeSlot> Homes => _homes ??= BuildHomes(Template);

    // Moves everything a floating window holds back into the main layout, so dismissing the
    // window docks its panels instead of dropping them.
    public void ReturnFloatingDockablesHome(IDockWindow window)
    {
        if (window.Layout is not { } floating || window.Owner is not IRootDock main)
        {
            return;
        }

        foreach (var dockable in Snapshot(floating))
        {
            ReturnHome(dockable, main);
        }
    }

    private void ReturnHome(IDockable dockable, IRootDock main)
    {
        if (dockable is IProportionalDockSplitter)
        {
            return;
        }

        if (dockable.Id is { Length: > 0 } id && Homes.TryGetValue(id, out var slot))
        {
            if (EnsureHome(slot.ParentId, main) is not { } container)
            {
                return;
            }

            // collapse:false - collapsing the emptied floating root would close the very window
            // whose Closing handler this runs inside.
            RemoveDockable(dockable, collapse: false);
            Insert(container, dockable, slot.Order);
            return;
        }

        // Anything else is a container the user built by dropping panels together: unwrap it
        // and send each panel home on its own.
        if (dockable is IDock dock)
        {
            foreach (var child in Snapshot(dock))
            {
                ReturnHome(child, main);
            }
        }
    }

    // Returns the dock a panel belongs in, rebuilding it when detaching its last panel left it
    // collapsed - and, recursively, whatever used to hold that dock.
    private IDock? EnsureHome(string parentId, IRootDock main)
    {
        if (parentId == StudioDockIds.Root)
        {
            return main;
        }

        if (FindDocked(main, dockable => dockable.Id == parentId) is IDock existing)
        {
            return existing;
        }

        if (FindDocked(Template, dockable => dockable.Id == parentId) is not IDock template
            || !Homes.TryGetValue(parentId, out var slot)
            || EnsureHome(slot.ParentId, main) is not { } owner
            || CreateEmptyLike(template) is not { } container)
        {
            return null;
        }

        Insert(owner, container, slot.Order);
        ReclaimStrays(container, main);
        return container;
    }

    // Emptying a row makes Dock hoist the single panel left in it up into the row's own parent.
    // Once the row is back, take those panels back with it so the layout reads as it did.
    private void ReclaimStrays(IDock container, IRootDock main)
    {
        if (container.Id is not { Length: > 0 } containerId)
        {
            return;
        }

        var strays = FindAllDocked(main, dockable =>
            dockable.Id is { Length: > 0 } id
            && Homes.TryGetValue(id, out var slot)
            && slot.ParentId == containerId
            && !ReferenceEquals(dockable.Owner, container));

        foreach (var stray in strays)
        {
            RemoveDockable(stray, collapse: false);
            Insert(container, stray, OrderOf(stray));
        }
    }

    private void Insert(IDock container, IDockable dockable, int order)
    {
        container.VisibleDockables ??= CreateList<IDockable>();
        var items = container.VisibleDockables;

        var index = items.Count;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is not IProportionalDockSplitter && OrderOf(items[i]) > order)
            {
                index = i;
                break;
            }
        }

        // Children of a proportional dock are separated by a splitter, or they cannot be resized.
        var needsSplitter = container is IProportionalDock && items.Count > 0;
        if (needsSplitter && index == items.Count)
        {
            InsertSplitter(container, index);
            index++;
        }

        InsertVisibleDockable(container, index, dockable);
        OnDockableAdded(dockable);
        InitDockable(dockable, container);

        if (needsSplitter && index + 1 < items.Count)
        {
            InsertSplitter(container, index + 1);
        }

        container.ActiveDockable ??= dockable;

        if (container is IRootDock root)
        {
            root.DefaultDockable ??= dockable;
        }
    }

    private void InsertSplitter(IDock container, int index)
    {
        var splitter = CreateProportionalDockSplitter();
        InsertVisibleDockable(container, index, splitter);
        InitDockable(splitter, container);
    }

    private IDock? CreateEmptyLike(IDock template)
    {
        IDock created;
        switch (template)
        {
            case IProportionalDock proportional:
                var proportionalDock = CreateProportionalDock();
                proportionalDock.Orientation = proportional.Orientation;
                created = proportionalDock;
                break;
            case IDocumentDock document:
                var documentDock = CreateDocumentDock();
                documentDock.CanCreateDocument = document.CanCreateDocument;
                created = documentDock;
                break;
            case IToolDock tool:
                var toolDock = CreateToolDock();
                toolDock.Alignment = tool.Alignment;
                created = toolDock;
                break;
            default:
                return null;
        }

        created.Id = template.Id;
        created.Title = template.Title;
        created.Proportion = template.Proportion;
        created.CanClose = template.CanClose;
        created.CanFloat = template.CanFloat;
        created.VisibleDockables = CreateList<IDockable>();
        return created;
    }

    private int OrderOf(IDockable dockable)
        => dockable.Id is { Length: > 0 } id && Homes.TryGetValue(id, out var slot) ? slot.Order : int.MaxValue;

    private static Dictionary<string, HomeSlot> BuildHomes(IRootDock template)
    {
        var homes = new Dictionary<string, HomeSlot>();
        Walk(template);
        return homes;

        void Walk(IDock parent)
        {
            if (parent.VisibleDockables is not { } children || parent.Id is not { Length: > 0 } parentId)
            {
                return;
            }

            var order = 0;
            foreach (var child in children)
            {
                if (child is IProportionalDockSplitter)
                {
                    continue;
                }

                if (child.Id is { Length: > 0 } id)
                {
                    homes[id] = new HomeSlot(parentId, order);
                }

                order++;

                if (child is IDock childDock)
                {
                    Walk(childDock);
                }
            }
        }
    }

    // FindDockable also reaches into floating windows; homing only ever means the docked tree.
    private static IDockable? FindDocked(IDock scope, Func<IDockable, bool> predicate)
    {
        if (predicate(scope))
        {
            return scope;
        }

        foreach (var child in Snapshot(scope))
        {
            if (predicate(child))
            {
                return child;
            }

            if (child is IDock childDock && FindDocked(childDock, predicate) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static List<IDockable> FindAllDocked(IDock scope, Func<IDockable, bool> predicate)
    {
        var found = new List<IDockable>();
        Walk(scope);
        return found;

        void Walk(IDock dock)
        {
            foreach (var child in Snapshot(dock))
            {
                if (predicate(child))
                {
                    found.Add(child);
                }

                if (child is IDock childDock)
                {
                    Walk(childDock);
                }
            }
        }
    }

    private static List<IDockable> Snapshot(IDock dock)
        => dock.VisibleDockables is { } dockables ? new List<IDockable>(dockables) : new List<IDockable>();
}
