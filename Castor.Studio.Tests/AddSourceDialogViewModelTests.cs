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

        Assert.Equal(7, viewModel.Categories.Count);
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
        Assert.Contains("caméras", viewModel.CatalogMessage.Text);
    }

    [Fact]
    public void Rtmp_camera_needs_an_address_before_it_can_be_added()
    {
        var viewModel = new AddSourceDialogViewModel(new FakeSourceRuntime(), null);

        viewModel.SelectCategoryCommand.Execute(
            viewModel.Categories.Single(category => category.Kind == AddSourceCategoryKind.NetworkCameras));

        Assert.True(viewModel.IsNetworkCategory);
        Assert.False(viewModel.IsListCategory);
        Assert.False(viewModel.CanConfirm);
        viewModel.NetworkUrl = "rtmp://192.168.1.20/live/camera";
        Assert.True(viewModel.CanConfirm);
    }

    [Theory]
    [InlineData("192.168.1.20/live/secret-key")]
    [InlineData("rtsp://192.168.1.20/live/secret-key")]
    [InlineData("http://192.168.1.20/live/secret-key")]
    [InlineData("rtmp://192.168.1.20:1935/")]
    public void Rtmp_camera_rejects_other_addresses_without_repeating_them(string url)
    {
        var viewModel = new AddSourceDialogViewModel(new FakeSourceRuntime(), null);
        viewModel.SelectCategoryCommand.Execute(
            viewModel.Categories.Single(category => category.Kind == AddSourceCategoryKind.NetworkCameras));
        AddSourceResult? result = null;
        viewModel.CloseRequested += closed => result = closed;

        viewModel.NetworkUrl = url;
        viewModel.ConfirmCommand.Execute(null);

        Assert.Null(result);
        Assert.Contains("rtmp://", viewModel.NetworkError);
        Assert.DoesNotContain("secret-key", viewModel.NetworkError);

        viewModel.NetworkUrl = "rtmp://192.168.1.20/live/secret-key";
        Assert.Empty(viewModel.NetworkError);
    }

    [Theory]
    [InlineData("", "Caméra RTMP")]
    [InlineData("  Plateau  ", "Plateau")]
    public void Rtmp_camera_returns_the_trimmed_address_and_a_name(string name, string expectedName)
    {
        var viewModel = new AddSourceDialogViewModel(new FakeSourceRuntime(), null);
        viewModel.SelectCategoryCommand.Execute(
            viewModel.Categories.Single(category => category.Kind == AddSourceCategoryKind.NetworkCameras));
        AddSourceResult? result = null;
        viewModel.CloseRequested += closed => result = closed;

        viewModel.NetworkName = name;
        viewModel.NetworkUrl = "  RTMPS://live.example.com:443/app/secret-key ";
        viewModel.ConfirmCommand.Execute(null);

        var network = Assert.IsType<AddSourceResult.Network>(result);
        Assert.Equal(expectedName, network.Name);
        Assert.Equal("RTMPS://live.example.com:443/app/secret-key", network.Url);
        Assert.DoesNotContain("secret-key", network.ToString());
    }

    [Fact]
    public void Rtmp_camera_cannot_be_added_while_libobs_is_unavailable()
    {
        var viewModel = new AddSourceDialogViewModel(new FakeSourceRuntime { IsAvailable = false }, null);
        viewModel.SelectCategoryCommand.Execute(
            viewModel.Categories.Single(category => category.Kind == AddSourceCategoryKind.NetworkCameras));
        AddSourceResult? result = null;
        viewModel.CloseRequested += closed => result = closed;

        viewModel.NetworkUrl = "rtmp://192.168.1.20/live/camera";
        viewModel.ConfirmCommand.Execute(null);

        Assert.False(viewModel.CanConfirm);
        Assert.Null(result);
    }

    [Fact]
    public async Task Scan_lists_rtmp_devices_and_picking_one_fills_in_its_server()
    {
        var discovery = new FakeDiscovery(
        [
            new DiscoveredRtmpServer("127.0.0.1", "Cet ordinateur"),
            new DiscoveredRtmpServer("192.168.1.20")
        ]);
        var viewModel = new AddSourceDialogViewModel(new FakeSourceRuntime(), null, discovery: discovery);

        await viewModel.ScanNetworkCommand.ExecuteAsync(null);

        Assert.Equal(["Cet ordinateur", "192.168.1.20"], viewModel.DiscoveredServers.Select(server => server.Title));
        Assert.Contains("2 appareil(s)", viewModel.ScanMessage);
        Assert.False(viewModel.IsScanning);

        viewModel.SelectedServer = viewModel.DiscoveredServers[1];
        Assert.Equal("rtmp://192.168.1.20:1935/", viewModel.NetworkUrl);
    }

    [Fact]
    public async Task Scan_says_when_nothing_answers_and_a_new_scan_starts_from_scratch()
    {
        var discovery = new FakeDiscovery([new DiscoveredRtmpServer("192.168.1.20")]);
        var viewModel = new AddSourceDialogViewModel(new FakeSourceRuntime(), null, discovery: discovery);
        await viewModel.ScanNetworkCommand.ExecuteAsync(null);
        viewModel.SelectedServer = viewModel.DiscoveredServers[0];

        discovery.Servers = [];
        await viewModel.ScanNetworkCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.DiscoveredServers);
        Assert.Null(viewModel.SelectedServer);
        Assert.Contains("Aucun appareil", viewModel.ScanMessage);
    }

    [Fact]
    public async Task Scan_failure_is_reported()
    {
        var discovery = new FakeDiscovery([]) { Failure = new InvalidOperationException("réseau absent") };
        var viewModel = new AddSourceDialogViewModel(new FakeSourceRuntime(), null, discovery: discovery);

        await viewModel.ScanNetworkCommand.ExecuteAsync(null);

        Assert.Equal("Scan impossible : réseau absent", viewModel.ScanMessage);
        Assert.False(viewModel.IsScanning);
    }

    [Fact]
    public void Scan_is_unavailable_without_a_discovery_service()
    {
        var viewModel = new AddSourceDialogViewModel(new FakeSourceRuntime(), null);

        Assert.False(viewModel.CanScan);
        Assert.False(viewModel.ScanNetworkCommand.CanExecute(null));
    }

    private sealed class FakeDiscovery(IReadOnlyList<DiscoveredRtmpServer> servers) : IRtmpDiscoveryService
    {
        public IReadOnlyList<DiscoveredRtmpServer> Servers { get; set; } = servers;
        public Exception? Failure { get; init; }

        public Task<IReadOnlyList<DiscoveredRtmpServer>> ScanAsync(CancellationToken cancellationToken) =>
            Failure != null ? Task.FromException<IReadOnlyList<DiscoveredRtmpServer>>(Failure) : Task.FromResult(Servers);
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
        public SourceRuntimeResult RenameSource(Guid sceneId, Guid sourceId, string requestedName) =>
            SourceRuntimeResult.Success(requestedName);
        public SourceOrderResult GetSourceOrder(Guid sceneId) => SourceOrderResult.Success([]);
        public SourceRuntimeResult MoveSource(Guid sceneId, Guid sourceId, int layerIndex) => SourceRuntimeResult.Success();

        public SceneCompositionResult GetSceneComposition(Guid sceneId) =>
            SceneCompositionResult.Success(SceneComposition.Empty);

        public SourceTransformResult SetSourceTransform(Guid sceneId, Guid sourceId, SourcePlacement placement) =>
            SourceTransformResult.Failure("Aucune source à transformer.");

        public SourceTransformResult SetSourceZoom(Guid sceneId, Guid sourceId, SourceZoom zoom) =>
            SourceTransformResult.Failure("Aucune source à zoomer.");
    }
}
