using System;
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

    /// <summary>Pointeur vers le scene_t* natif. IntPtr.Zero si non initialisé.</summary>
    public IntPtr NativePtr { get; set; } = IntPtr.Zero;

    public SceneItem() { }

    public SceneItem(string name, bool isActive = false, bool isLive = false)
    {
        Name = name;
        IsActive = isActive;
        IsLive = isLive;
    }
}
