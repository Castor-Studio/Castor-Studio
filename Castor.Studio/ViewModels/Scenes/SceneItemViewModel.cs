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
    private bool _isSelected;

    [ObservableProperty]
    private bool _isLive;

    // Shown in the scene lists, and blocks deletion: a recording is writing this scene.
    [ObservableProperty]
    private bool _isBeingRecorded;

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
        Sources.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasVideoSource));
    }

    // A scene without a picture sends black on air: the Studio band flags it.
    public bool HasVideoSource => Sources.Any(source => source.Kind is SourceKind.Video or SourceKind.Media);

    public SceneDefinition ToDefinition() => new()
    {
        Id = Id,
        CreatedAt = CreatedAt,
        Name = Name,
        Color = Color,
        Sources = Sources.Select(source => source.ToDefinition()).ToList()
    };
}
