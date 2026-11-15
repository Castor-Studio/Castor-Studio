using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Input;

namespace CastorApplication.ViewModels.Settings;

public sealed partial class SettingsSectionItem : ViewModelBase
{
    [ObservableProperty]
    private bool isSelected;

    public required string Title { get; init; }

    public required ViewModelBase ViewModel { get; init; }

    public required ICommand SelectCommand { get; init; }
}
