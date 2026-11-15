using CastorApplication.ViewModels.Scenes;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CastorApplication.ViewModels.Studio;

// A scene that can be recorded into a file of its own, next to (or instead of) the program.
public sealed partial class RecordingSceneOption(SceneItemViewModel scene) : ViewModelBase
{
    public SceneItemViewModel Scene { get; } = scene;

    [ObservableProperty]
    private bool _isSelected;
}
