using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace CastorApplication.ViewModels;

public partial class StudioViewModel : ViewModelBase
{
    // C'est ici que le Layout sera stocké
    [ObservableProperty]
    private IRootDock? _layout;
}