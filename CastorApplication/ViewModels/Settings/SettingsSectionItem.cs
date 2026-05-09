using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
