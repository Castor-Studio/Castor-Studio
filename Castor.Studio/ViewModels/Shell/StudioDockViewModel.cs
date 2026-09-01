using CastorApplication.Docking;
using CastorApplication.Services.Settings;
using CastorApplication.ViewModels.Multicam;
using CastorApplication.ViewModels.Scenes;
using CastorApplication.ViewModels.Studio;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace CastorApplication.ViewModels.Shell;

public partial class StudioDockViewModel : ViewModelBase
{
    private readonly IFactory _factory;
    private readonly DockLayoutService _layoutService;

    [ObservableProperty] private IRootDock? _layout;
    [ObservableProperty] private string? _focusedPaneId;

    internal StudioDockViewModel(
        StudioViewModel studioViewModel,
        ScenesViewModel scenesViewModel,
        MulticamViewModel multicamViewModel,
        DockLayoutService layoutService)
    {
        _layoutService = layoutService;
        _factory = new StudioDockFactory(studioViewModel, scenesViewModel, multicamViewModel);
        _factory.FocusedDockableChanged += (_, args) => FocusedPaneId = args.Dockable?.Id;

        var layout = _layoutService.Load() as IRootDock ?? _factory.CreateLayout();
        _factory.InitLayout(layout!);
        Layout = layout;
    }

    public void FocusPane(string id)
    {
        if (Layout is not { } root) return;
        if (_factory.FindDockable(root, d => d.Id == id) is not { } dockable) return;
        _factory.SetActiveDockable(dockable);
        _factory.SetFocusedDockable(root, dockable);
    }

    public void SaveLayout()
    {
        if (Layout is { } root) _layoutService.Save(root);
    }
}
