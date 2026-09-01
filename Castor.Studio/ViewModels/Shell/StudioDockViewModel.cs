using CastorApplication.Docking;
using CastorApplication.Services.Settings;
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

    internal StudioDockViewModel(StudioViewModel studioViewModel, DockLayoutService layoutService)
    {
        _layoutService = layoutService;
        _factory = new StudioDockFactory(studioViewModel);

        var layout = _layoutService.Load() as IRootDock ?? _factory.CreateLayout();
        _factory.InitLayout(layout!);
        Layout = layout;
    }

    public void SaveLayout()
    {
        if (Layout is { } root) _layoutService.Save(root);
    }
}
