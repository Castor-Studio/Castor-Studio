using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Castor.Engine.Models;

public partial class SceneItem : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isLive;

    [ObservableProperty]
    private string _color = "#5b8def";

    /// <summary>Coché par l'utilisateur pour une action groupée (suppression, etc.) — état UI uniquement.</summary>
    [ObservableProperty]
    private bool _isMultiSelected;

    public ObservableCollection<SourceItem> Sources { get; } = new();

    internal IntPtr NativePtr { get; set; } = IntPtr.Zero;

    public SceneItem()
    {
    }

    public SceneItem(string name, bool isActive = false, bool isLive = false)
    {
        Name = name;
        IsActive = isActive;
        IsLive = isLive;
    }
}
