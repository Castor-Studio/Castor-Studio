using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CastorApplication.Models;

public partial class SceneItem : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isLive;

    public ObservableCollection<SourceItem> Sources { get; } = new();

    public SceneItem() { }

    public SceneItem(string name, bool isActive = false, bool isLive = false)
    {
        Name = name;
        IsActive = isActive;
        IsLive = isLive;
    }
}
