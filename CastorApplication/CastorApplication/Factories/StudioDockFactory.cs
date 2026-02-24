using System;
using System.Collections.Generic;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;

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
        // 1. On crée les contenus (les onglets)
        var videoView = new Document { Id = "Video", Title = "Preview Vidéo" };
        var sourcesView = new Tool { Id = "Sources", Title = "Sources" };
        var audioView = new Tool { Id = "Audio", Title = "Mixer Audio" };

        // 2. On organise les panneaux
        var mainLayout = new ProportionalDock
        {
            Id = "MainLayout",
            Orientation = Orientation.Horizontal, // Séparation gauche/droite
            VisibleDockables = CreateList<IDockable>(
                // Zone de gauche (Vidéo)
                new DocumentDock
                {
                    Id = "VideoPane",
                    ActiveDockable = videoView,
                    VisibleDockables = CreateList<IDockable>(videoView),
                    Proportion = 0.7 // Prend 70% de l'espace
                },
                // Splitter (automatique entre deux zones)
                // Zone de droite (Outils)
                new ToolDock
                {
                    Id = "ToolsPane",
                    ActiveDockable = sourcesView,
                    VisibleDockables = CreateList<IDockable>(sourcesView, audioView),
                    Proportion = 0.3 // Prend 30% de l'espace
                }
            )
        };

        // 3. Le Root (le conteneur maître)
        var root = CreateRootDock();
        root.Id = "Root";
        root.ActiveDockable = mainLayout;
        root.VisibleDockables = CreateList<IDockable>(mainLayout);

        return root;
    }

    public override void InitLayout(IDockable layout)
    {
        // On lie le contexte (ton ViewModel) pour que les boutons fonctionnent
        this.ContextLocator = new Dictionary<string, Func<object?>>
        {
            ["Root"] = () => _context,
            ["Video"] = () => _context,
            ["Sources"] = () => _context,
            ["Audio"] = () => _context
        };
        base.InitLayout(layout);
    }
}