using CastorApplication.Models.Studio;
using CastorApplication.Services.Studio;
using CastorApplication.ViewModels.Scenes;

namespace Castor.Studio.Tests;

public sealed class AddSourceDialogViewModelTests
{
    [Fact]
    public async Task Refresh_groups_catalog_counts_and_marks_existing_source_by_stable_id()
    {
        var scene = new SceneItemViewModel(new SceneDefinition
        {
            Name = "Scène",
            Sources =
            [
                new SourceDefinition
                {
                    Name = "Ancien libellé",
                    Kind = SourceKind.Video,
                    Origin = SourceOrigin.HardwareVideo,
                    OriginPath = "window-1"
                }
            ]
        });
        var runtime = new FakeSourceRuntime
        {
            Catalog = new SourceCatalog(
            [
                new("monitor-1", "Écran principal", VideoCaptureKind.Monitor),
                new("window-1", "Navigateur renommé", VideoCaptureKind.Window),
                new("camera-1", "Webcam", VideoCaptureKind.Camera)
            ],
            [
                new("output-1", "Haut-parleurs", AudioCaptureKind.LoopbackGlobal),
                new("input-1", "Microphone", AudioCaptureKind.Microphone)
            ])
        };
        var viewModel = new AddSourceDialogViewModel(runtime, scene);

        await viewModel.Refresh(CancellationToken.None);

        Assert.Equal(6, viewModel.Categories.Count);
        Assert.Equal(1, viewModel.Categories.Single(category => category.Kind == AddSourceCategoryKind.Monitors).Count);
        Assert.Equal(1, viewModel.Categories.Single(category => category.Kind == AddSourceCategoryKind.Windows).Count);
        Assert.Equal(1, viewModel.Categories.Single(category => category.Kind == AddSourceCategoryKind.Cameras).Count);
        var window = Assert.Single(viewModel.VisibleItems);
        Assert.Equal("Navigateur renommé", window.Title);
        Assert.True(window.AlreadyInScene);
    }

    [Fact]
    public async Task Search_filters_current_category_and_files_expose_one_media_choice()
    {
        var runtime = new FakeSourceRuntime
        {
            Catalog = new SourceCatalog(
            [
                new("window-1", "Navigateur", VideoCaptureKind.Window),
                new("window-2", "Terminal", VideoCaptureKind.Window)
            ], [])
        };
        var viewModel = new AddSourceDialogViewModel(runtime, null);
        await viewModel.Refresh(CancellationToken.None);

        viewModel.SearchText = "term";
        Assert.Equal("Terminal", Assert.Single(viewModel.VisibleItems).Title);

        viewModel.SearchText = "";
        viewModel.SelectCategoryCommand.Execute(
            viewModel.Categories.Single(category => category.Kind == AddSourceCategoryKind.Files));
        var media = Assert.Single(viewModel.VisibleItems);
        Assert.IsType<AddSourceResult.Media>(media.FixedResult);
    }

    [Fact]
    public async Task Refresh_surfaces_partial_catalog_errors_without_hiding_available_items()
    {
        var runtime = new FakeSourceRuntime
        {
            Catalog = new SourceCatalog(
                [new("window-1", "Fenêtre", VideoCaptureKind.Window)],
                [],
                "Énumération caméras impossible")
        };
        var viewModel = new AddSourceDialogViewModel(runtime, null);

        await viewModel.Refresh(CancellationToken.None);

        Assert.Single(viewModel.VisibleItems);
        Assert.Contains("caméras", viewModel.CatalogMessage);
    }

    private sealed class FakeSourceRuntime : ISourceRuntime
    {
        public SourceCatalog Catalog { get; init; } = new([], []);
        public bool IsAvailable { get; init; } = true;
        public string UnavailableMessage => IsAvailable ? "" : "LibObs indisponible";

        public Task<SourceCatalog> EnumerateSourcesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Catalog);

        public SourceRuntimeResult AddSource(Guid sceneId, SourceAddRequest request) =>
            SourceRuntimeResult.Success(request.RequestedName);

        public SourceRuntimeResult RemoveSource(Guid sceneId, Guid sourceId) => SourceRuntimeResult.Success();
        public SourceRuntimeResult SetMediaLoop(Guid sceneId, Guid sourceId, bool loop) => SourceRuntimeResult.Success();
    }
}
