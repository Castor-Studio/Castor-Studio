using System.Collections.ObjectModel;
using CastorApplication.Models.Studio;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CastorApplication.ViewModels.Scenes;

public partial class SceneItemViewModel : ViewModelBase
{
    public Guid Id { get; }
    public DateTime CreatedAt { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isLive;

    [ObservableProperty]
    private string _color;

    [ObservableProperty]
    private bool _isMultiSelected;

    public ObservableCollection<SourceItemViewModel> Sources { get; }

    public SceneItemViewModel(SceneDefinition scene)
    {
        Id = scene.Id;
        CreatedAt = scene.CreatedAt;
        _name = scene.Name;
        _color = scene.Color;
        Sources = new ObservableCollection<SourceItemViewModel>(scene.Sources.Select(source => new SourceItemViewModel(source)));
    }

    public SceneDefinition ToDefinition() => new()
    {
        Id = Id,
        CreatedAt = CreatedAt,
        Name = Name,
        Color = Color,
        Sources = Sources.Select(source => source.ToDefinition()).ToList()
    };
}
