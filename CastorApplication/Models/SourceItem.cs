using CommunityToolkit.Mvvm.ComponentModel;

namespace CastorApplication.Models;

public partial class SourceItem : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _type = "Vidéo"; // "Vidéo" or "Audio"

    [ObservableProperty]
    private string _color = "#c96cc0";

    [ObservableProperty]
    private bool _isActive = true;

    public SourceItem() { }

    public SourceItem(string name, string type, string color)
    {
        Name = name;
        Type = type;
        Color = color;
    }
}
