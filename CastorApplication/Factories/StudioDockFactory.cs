using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CastorApplication.Factories;

public class StudioDockFactory : Factory
{
    private readonly object _context;

    public StudioDockFactory(object context)
    {
        _context = context;
    }

    public override IRootDock CreateLayout()
    {
        // 1. On crée le contenu (preview uniquement, Sources et Audio sont dans la barre du bas)
        var videoView = new Document { Id = "Video", Title = "Preview Vidéo", CanFloat = false };

        // 2. On organise le panneau principal
        var mainLayout = new ProportionalDock
        {
            Id = "MainLayout",
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>(
                new DocumentDock
                {
                    Id = "VideoPane",
                    ActiveDockable = videoView,
                    VisibleDockables = CreateList<IDockable>(videoView),
                    CanFloat = false
                }
            )
        };

        // 3. Le Root (le conteneur maître)
        var root = CreateRootDock();
        root.Id = "Root";
        root.ActiveDockable = mainLayout;
        root.VisibleDockables = CreateList<IDockable>(mainLayout);
        root.CanFloat = false;

        return root;
    }

    public override void InitLayout(IDockable layout)
    {
        // On lie le contexte (ton ViewModel) pour que les boutons fonctionnent
        this.ContextLocator = new Dictionary<string, Func<object?>>
        {
            ["Root"] = () => _context,
            ["Video"] = () => _context
        };
        base.InitLayout(layout);
    }
}