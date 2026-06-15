using CommunityToolkit.Mvvm.ComponentModel;

namespace CastorApplication.Models;

public partial class CameraItem : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _label = "";

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isLive;

    public CameraItem() { }

    public CameraItem(string label, string name, bool isActive = false, bool isLive = false)
    {
        Label = label;
        Name = name;
        IsActive = isActive;
        IsLive = isLive;
    }
}
