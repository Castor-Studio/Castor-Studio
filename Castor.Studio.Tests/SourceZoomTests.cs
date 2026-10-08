using CastorApplication.Models.Studio;
using CastorApplication.Services.Ai;
using CastorApplication.Services.Studio;
using CastorApplication.ViewModels.Scenes;

namespace Castor.Studio.Tests;

public sealed class SourceZoomTests
{
    [Fact]
    public void Without_zoom_nothing_more_is_cropped()
    {
        Assert.Equal(SourceCrop.None, SourceZoom.None.CropWithin(1920, 1080));
        Assert.Equal(SourceCrop.None, new SourceZoom(4).CropWithin(0, 0));
    }

    [Fact]
    public void A_centred_zoom_keeps_the_middle_of_the_image()
    {
        // ×2 sur 1920×1080 : une fenêtre de 960×540 au milieu.
        Assert.Equal(new SourceCrop(480, 270, 480, 270), new SourceZoom(2).CropWithin(1920, 1080));
    }

    [Fact]
    public void A_zoom_aimed_near_an_edge_is_pushed_back_inside_the_image()
    {
        // Le coin haut-gauche visé : la fenêtre se cale contre ce coin au lieu d'en sortir.
        Assert.Equal(new SourceCrop(0, 0, 960, 540), new SourceZoom(2, 0, 0).CropWithin(1920, 1080));
        Assert.Equal(new SourceCrop(960, 540, 0, 0), new SourceZoom(2, 1, 1).CropWithin(1920, 1080));
    }

    [Fact]
    public void Sliding_the_zoom_stops_where_the_window_touches_the_edge_of_the_image()
    {
        var camera = new SourceTransform(Guid.NewGuid(), 0, 0, 640, 360, 1, 1, SourceCrop.None, 640, 360, true)
        {
            Zoom = new SourceZoom(2)
        };

        // Un cadre entier tiré vers la gauche dépasse le bord droit : le point visé s'y arrête.
        Assert.Equal(new SourceZoom(2, 0.75, 0.5), CompositionGeometry.Pan(camera, -640, 0));

        // Sur une source tournée d'un quart de tour, tirer vers le bas du canvas, c'est tirer
        // le long de sa largeur.
        var turned = camera with { Rotation = 90 };
        Assert.Equal(new SourceZoom(2, 0.375, 0.5), CompositionGeometry.Pan(turned, 0, 160));
    }

    [Fact]
    public void An_ai_zoom_on_a_source_goes_to_the_engine_and_a_whole_scene_is_refused_for_now()
    {
        var runtime = new ZoomRecordingRuntime();
        var entryPoint = new AiZoomEntryPoint(runtime);
        var sceneId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();

        Assert.True(entryPoint.Apply(new AiZoomRequest(sceneId, sourceId, new SourceZoom(2))).IsSuccess);
        Assert.Equal((sceneId, sourceId, new SourceZoom(2)), Assert.Single(runtime.Zooms));

        Assert.False(entryPoint.Apply(new AiZoomRequest(sceneId, null, new SourceZoom(2))).IsSuccess);
        Assert.Single(runtime.Zooms);
    }

    private sealed class ZoomRecordingRuntime : ISourceRuntime
    {
        public List<(Guid SceneId, Guid SourceId, SourceZoom Zoom)> Zooms { get; } = [];

        public bool IsAvailable => true;
        public string UnavailableMessage => "";

        public SourceTransformResult SetSourceZoom(Guid sceneId, Guid sourceId, SourceZoom zoom)
        {
            Zooms.Add((sceneId, sourceId, zoom));
            return SourceTransformResult.Success(
                new SourceTransform(sourceId, 0, 0, 1920, 1080, 1, 1, SourceCrop.None, 1920, 1080, true) { Zoom = zoom });
        }

        public Task<SourceCatalog> EnumerateSourcesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SourceCatalog([], []));

        public SourceRuntimeResult AddSource(Guid sceneId, SourceAddRequest request) => SourceRuntimeResult.Success();
        public SourceRuntimeResult RemoveSource(Guid sceneId, Guid sourceId) => SourceRuntimeResult.Success();
        public SourceRuntimeResult SetMediaLoop(Guid sceneId, Guid sourceId, bool loop) => SourceRuntimeResult.Success();
        public SourceRuntimeResult RenameSource(Guid sceneId, Guid sourceId, string requestedName) => SourceRuntimeResult.Success();
        public SourceOrderResult GetSourceOrder(Guid sceneId) => SourceOrderResult.Success([]);
        public SourceRuntimeResult MoveSource(Guid sceneId, Guid sourceId, int layerIndex) => SourceRuntimeResult.Success();
        public SceneCompositionResult GetSceneComposition(Guid sceneId) => SceneCompositionResult.Success(SceneComposition.Empty);

        public SourceTransformResult SetSourceTransform(Guid sceneId, Guid sourceId, SourcePlacement placement) =>
            SourceTransformResult.Failure("Aucune source à transformer.");
    }
}
