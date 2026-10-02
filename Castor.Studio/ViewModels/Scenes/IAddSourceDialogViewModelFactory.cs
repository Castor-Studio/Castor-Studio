using CastorApplication.Services;
using CastorApplication.Services.Studio;

namespace CastorApplication.ViewModels.Scenes;

internal interface IAddSourceDialogViewModelFactory
{
    AddSourceDialogViewModel Create(SceneItemViewModel? scene);
}

internal sealed class AddSourceDialogViewModelFactory(ISourceRuntime runtime, StatusMessageService messages) : IAddSourceDialogViewModelFactory
{
    public AddSourceDialogViewModel Create(SceneItemViewModel? scene) => new(runtime, scene, messages);
}
