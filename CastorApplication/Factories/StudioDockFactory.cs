using CastorApplication.ViewModels;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using System;
using System.Collections.Generic;

namespace CastorApplication.Factories;

public class StudioDockFactory : Factory
{
    private readonly StudioViewModel _vm;
    private SourcesPanelContext? _sourcesCtx;
    private AudioPanelContext?   _audioCtx;

    public StudioDockFactory(StudioViewModel vm)
    {
        _vm = vm;
    }

    public override IRootDock CreateLayout()
    {
        _sourcesCtx = new SourcesPanelContext(_vm);
        _audioCtx   = new AudioPanelContext(_vm);

        var videoDoc = new Document
        {
            Id       = "Video",
            Title    = "Aperçu Vidéo",
            Context  = _vm,
            CanFloat = false,
            CanClose = false,
        };

        var sourcesTool = new Tool
        {
            Id       = "Sources",
            Title    = "Sources",
            Context  = _sourcesCtx,
            CanFloat = false,
            CanClose = false,
        };

        var audioTool = new Tool
        {
            Id       = "Audio",
            Title    = "Mixer Audio",
            Context  = _audioCtx,
            CanFloat = false,
            CanClose = false,
        };

        var rightDock = new ProportionalDock
        {
            Id          = "RightDock",
            Orientation = Orientation.Vertical,
            Proportion  = 0.28,
            VisibleDockables = CreateList<IDockable>(
                new ToolDock
                {
                    Id               = "SourcesDock",
                    ActiveDockable   = sourcesTool,
                    VisibleDockables = CreateList<IDockable>(sourcesTool),
                    CanFloat         = false,
                    CanClose         = false,
                    Proportion       = 0.5,
                },
                new ProportionalDockSplitter(),
                new ToolDock
                {
                    Id               = "AudioDock",
                    ActiveDockable   = audioTool,
                    VisibleDockables = CreateList<IDockable>(audioTool),
                    CanFloat         = false,
                    CanClose         = false,
                    Proportion       = 0.5,
                }
            ),
        };

        var videoPane = new DocumentDock
        {
            Id                = "VideoPane",
            ActiveDockable    = videoDoc,
            VisibleDockables  = CreateList<IDockable>(videoDoc),
            CanFloat          = false,
            CanClose          = false,
            CanCreateDocument = false,
        };

        var mainLayout = new ProportionalDock
        {
            Id          = "MainLayout",
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>(
                videoPane,
                new ProportionalDockSplitter(),
                rightDock
            ),
        };

        var root = CreateRootDock();
        root.Id               = "Root";
        root.ActiveDockable   = mainLayout;
        root.VisibleDockables = CreateList<IDockable>(mainLayout);
        root.CanFloat         = false;

        return root;
    }

    public override void InitLayout(IDockable layout)
    {
        ContextLocator = new Dictionary<string, Func<object?>>
        {
            ["Root"]    = () => _vm,
            ["Video"]   = () => _vm,
            ["Sources"] = () => _sourcesCtx,
            ["Audio"]   = () => _audioCtx,
        };
        base.InitLayout(layout);
    }
}
