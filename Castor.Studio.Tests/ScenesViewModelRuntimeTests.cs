using CastorApplication.Models.Studio;
using CastorApplication.Services;
using CastorApplication.Services.Dialogs;
using CastorApplication.Services.Studio;
using CastorApplication.ViewModels.Scenes;
using CastorApplication.ViewModels.Studio;

namespace Castor.Studio.Tests;

public sealed class ScenesViewModelRuntimeTests
{
    [Fact]
    public void Create_uses_the_effective_native_name_and_preserves_input_on_failure()
    {
        var runtime = new FakeSceneRuntime
        {
            Create = (_, name) => SceneRuntimeResult.Success($"{name} 2")
        };
        var viewModel = CreateViewModel(runtime);

        viewModel.NewSceneName = "Scène";
        viewModel.CreateSceneCommand.Execute(null);

        var scene = Assert.Single(viewModel.Scenes);
        Assert.Equal("Scène 2", scene.Name);
        Assert.Equal("", viewModel.NewSceneName);

        runtime.Create = (_, _) => SceneRuntimeResult.Failure("échec natif");
        viewModel.NewSceneName = "Refusée";
        viewModel.CreateSceneCommand.Execute(null);

        Assert.Single(viewModel.Scenes);
        Assert.Equal("Refusée", viewModel.NewSceneName);
        Assert.Equal("échec natif", viewModel.SceneIoStatus);
    }

    [Fact]
    public void Rename_changes_local_state_only_after_native_success()
    {
        var runtime = new FakeSceneRuntime();
        var viewModel = CreateViewModel(runtime);
        viewModel.NewSceneName = "Originale";
        viewModel.CreateSceneCommand.Execute(null);
        var scene = Assert.Single(viewModel.Scenes);

        viewModel.BeginRenameSceneCommand.Execute(scene);
        viewModel.RenameSceneName = "Demandée";
        runtime.Rename = (_, _) => SceneRuntimeResult.Failure("nom refusé");
        viewModel.ConfirmRenameSceneCommand.Execute(null);

        Assert.Equal("Originale", scene.Name);
        Assert.Same(scene, viewModel.SceneBeingRenamed);

        runtime.Rename = (_, _) => SceneRuntimeResult.Success("Demandée 2");
        viewModel.ConfirmRenameSceneCommand.Execute(null);

        Assert.Equal("Demandée 2", scene.Name);
        Assert.Null(viewModel.SceneBeingRenamed);
        Assert.Equal("", viewModel.SceneIoStatus);
    }

    [Fact]
    public void Delete_preserves_the_scene_and_active_selection_on_native_failure()
    {
        var runtime = new FakeSceneRuntime();
        var workspace = new StudioWorkspaceViewModel();
        var viewModel = CreateViewModel(runtime, workspace);
        var first = CreateScene(viewModel, "Première");
        var second = CreateScene(viewModel, "Deuxième");
        viewModel.SelectSceneCommand.Execute(second);

        runtime.Remove = _ => SceneRuntimeResult.Failure("suppression refusée");
        viewModel.DeleteSceneCommand.Execute(second);

        Assert.Equal(2, viewModel.Scenes.Count);
        Assert.Same(second, workspace.ActiveScene);
        Assert.Equal("suppression refusée", viewModel.DeleteSceneError);

        runtime.Remove = _ => SceneRuntimeResult.Success();
        viewModel.DeleteSceneCommand.Execute(second);

        Assert.Single(viewModel.Scenes);
        Assert.Same(first, workspace.ActiveScene);
        Assert.Equal("", viewModel.DeleteSceneError);
    }

    [Fact]
    public void Delete_guard_does_not_call_native_runtime_while_live()
    {
        var runtime = new FakeSceneRuntime();
        var workspace = new StudioWorkspaceViewModel();
        var viewModel = CreateViewModel(runtime, workspace);
        var scene = CreateScene(viewModel, "Live");
        workspace.SetStreamingState(true);

        viewModel.DeleteSceneCommand.Execute(scene);

        Assert.Single(viewModel.Scenes);
        Assert.Equal(0, runtime.RemoveCalls);
        Assert.Contains("seule scène", viewModel.DeleteSceneError);
    }

    [Fact]
    public void Bulk_delete_keeps_each_failed_scene_synchronized()
    {
        var runtime = new FakeSceneRuntime();
        var viewModel = CreateViewModel(runtime);
        var first = CreateScene(viewModel, "Première");
        var second = CreateScene(viewModel, "Deuxième");
        first.IsMultiSelected = true;
        second.IsMultiSelected = true;
        runtime.Remove = id => id == first.Id
            ? SceneRuntimeResult.Success()
            : SceneRuntimeResult.Failure("occupée");

        viewModel.DeleteSelectedScenesCommand.Execute(null);

        Assert.Same(second, Assert.Single(viewModel.Scenes));
        Assert.Contains("Deuxième", viewModel.DeleteSceneError);
        Assert.Contains("occupée", viewModel.DeleteSceneError);
    }

    [Fact]
    public async Task Import_creates_native_scenes_and_skips_each_failure()
    {
        var accepted = new SceneDefinition
        {
            Name = "Importée",
            Sources =
            [
                new SourceDefinition { Name = "Caméra", Origin = SourceOrigin.HardwareVideo },
                new SourceDefinition { Name = "Fichier", Origin = SourceOrigin.File }
            ]
        };
        var rejected = new SceneDefinition { Name = "Refusée" };
        var runtime = new FakeSceneRuntime
        {
            Create = (id, name) => id == rejected.Id
                ? SceneRuntimeResult.Failure("identifiant refusé")
                : SceneRuntimeResult.Success($"{name} 2")
        };
        var viewModel = CreateViewModel(runtime, imported: [accepted, rejected]);

        await viewModel.ImportScenesCommand.ExecuteAsync(null);

        var scene = Assert.Single(viewModel.Scenes);
        Assert.Equal("Importée 2", scene.Name);
        Assert.Single(scene.Sources);
        Assert.Equal("Fichier", scene.Sources[0].Name);
        Assert.Contains("1 scène(s) refusée(s)", viewModel.SceneIoStatus);
    }

    private static SceneItemViewModel CreateScene(ScenesViewModel viewModel, string name)
    {
        viewModel.NewSceneName = name;
        viewModel.CreateSceneCommand.Execute(null);
        return viewModel.Scenes[^1];
    }

    private static ScenesViewModel CreateViewModel(
        FakeSceneRuntime runtime,
        StudioWorkspaceViewModel? workspace = null,
        IReadOnlyList<SceneDefinition>? imported = null) =>
        new(
            workspace ?? new StudioWorkspaceViewModel(),
            new UnavailableStudioRuntime(),
            runtime,
            new FakeFilePicker(imported == null ? null : "scenes.json"),
            new FakeSceneCollection(imported ?? []),
            new FakeDialogFactory(),
            new FakeDialogService());

    private sealed class FakeSceneRuntime : ISceneRuntime
    {
        public Func<Guid, string, SceneRuntimeResult> Create { get; set; } =
            (_, name) => SceneRuntimeResult.Success(name);
        public Func<Guid, string, SceneRuntimeResult> Rename { get; set; } =
            (_, name) => SceneRuntimeResult.Success(name);
        public Func<Guid, SceneRuntimeResult> Remove { get; set; } =
            _ => SceneRuntimeResult.Success();

        public bool IsAvailable => true;
        public string UnavailableMessage => "";
        public int RemoveCalls { get; private set; }

        public SceneRuntimeResult CreateScene(Guid sceneId, string requestedName) => Create(sceneId, requestedName);
        public SceneRuntimeResult RenameScene(Guid sceneId, string requestedName) => Rename(sceneId, requestedName);

        public SceneRuntimeResult RemoveScene(Guid sceneId)
        {
            RemoveCalls++;
            return Remove(sceneId);
        }
    }

    private sealed class FakeFilePicker(string? importPath) : IFilePickerService
    {
        public Task<string?> PickRecordingOutputFileAsync(string extension = ".mp4", string formatLabel = "MP4 (H.264 + AAC)") => Task.FromResult<string?>(null);
        public Task<string?> PickVideoFileAsync() => Task.FromResult<string?>(null);
        public Task<string?> PickAudioFileAsync() => Task.FromResult<string?>(null);
        public Task<string?> PickSceneExportFileAsync() => Task.FromResult<string?>(null);
        public Task<string?> PickSceneImportFileAsync() => Task.FromResult(importPath);
    }

    private sealed class FakeSceneCollection(IReadOnlyList<SceneDefinition> scenes) : ISceneCollectionService
    {
        public Task SaveAsync(string path, IReadOnlyCollection<SceneDefinition> definitions, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<SceneDefinition>> LoadAsync(string path, CancellationToken cancellationToken) => Task.FromResult(scenes);
    }

    private sealed class FakeDialogFactory : IAddSourceDialogViewModelFactory
    {
        public AddSourceDialogViewModel Create(SceneItemViewModel? scene) => new(new UnavailableStudioRuntime(), scene);
    }

    private sealed class FakeDialogService : IAddSourceDialogService
    {
        public Task<AddSourceResult?> ShowAsync(AddSourceDialogViewModel viewModel) => Task.FromResult<AddSourceResult?>(null);
    }
}
