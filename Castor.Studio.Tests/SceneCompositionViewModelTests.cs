using CastorApplication.Models.Studio;
using CastorApplication.Services.Studio;
using CastorApplication.ViewModels.Scenes;

namespace Castor.Studio.Tests;

public sealed class SceneCompositionViewModelTests
{
    [Fact]
    public void Three_sources_are_drawn_at_the_transform_the_engine_holds()
    {
        var scene = SceneWith("Fond", "Caméra", "Overlay");
        var runtime = new FakeCompositionRuntime(scene.Id);
        // Premier plan en tête, comme partout ailleurs dans l'application.
        runtime.Compose(scene,
            Transform(scene, "Overlay", x: 1400, y: 820, width: 480, height: 240),
            Transform(scene, "Caméra", x: 960, y: 60, width: 640, height: 360,
                crop: new SourceCrop(40, 0, 40, 0)),
            Transform(scene, "Fond", x: 0, y: 0, width: 1920, height: 1080));

        var composition = new SceneCompositionViewModel(runtime);
        composition.ShowScene(scene);

        // Le canvas se peint de l'arrière vers l'avant : la liste rend l'empilement du
        // moteur dans le sens du dessin.
        Assert.Equal(["Fond", "Caméra", "Overlay"], composition.Layers.Select(layer => layer.Name));

        var camera = composition.Layers[1];
        Assert.Equal((960d, 60d, 640d, 360d), (camera.X, camera.Y, camera.Width, camera.Height));
        Assert.Contains("rognée", camera.Geometry);
        Assert.True(composition.HasLayers);
        Assert.Equal((1920d, 1080d), (composition.CanvasWidth, composition.CanvasHeight));
    }

    [Fact]
    public void Overlapping_sources_keep_the_stacking_order_of_the_engine()
    {
        var scene = SceneWith("Fond", "Overlay");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene,
            Transform(scene, "Overlay", x: 100, y: 100, width: 800, height: 600),
            Transform(scene, "Fond", x: 0, y: 0, width: 1920, height: 1080));

        var composition = new SceneCompositionViewModel(runtime);
        composition.ShowScene(scene);
        Assert.Equal(["Fond", "Overlay"], composition.Layers.Select(layer => layer.Name));

        // Le moteur remonte le fond devant : le canvas doit suivre, pas conserver son ordre.
        runtime.Compose(scene,
            Transform(scene, "Fond", x: 0, y: 0, width: 1920, height: 1080),
            Transform(scene, "Overlay", x: 100, y: 100, width: 800, height: 600));
        composition.Refresh();

        Assert.Equal(["Overlay", "Fond"], composition.Layers.Select(layer => layer.Name));
    }

    [Fact]
    public void Reopening_a_scene_redraws_it_from_what_the_engine_holds_now()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 0, y: 0, width: 1920, height: 1080));

        var composition = new SceneCompositionViewModel(runtime);
        composition.ShowScene(scene);
        Assert.Equal(1920d, composition.Layers[0].Width);

        // La transformation change du côté du moteur, sans que l'interface en soit prévenue.
        runtime.Compose(scene, Transform(scene, "Caméra", x: 320, y: 180, width: 1280, height: 720));

        composition.ShowScene(null);
        Assert.Empty(composition.Layers);
        Assert.Equal("Aucune scène sélectionnée.", composition.Placeholder);

        composition.ShowScene(scene);
        var layer = Assert.Single(composition.Layers);
        Assert.Equal((320d, 180d, 1280d, 720d), (layer.X, layer.Y, layer.Width, layer.Height));
    }

    [Fact]
    public void A_moved_source_keeps_its_layer_instead_of_being_drawn_again()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 0, y: 0, width: 640, height: 360));

        var composition = new SceneCompositionViewModel(runtime);
        composition.ShowScene(scene);
        var layer = Assert.Single(composition.Layers);

        // Le canvas se relit plusieurs fois par seconde : les calques sont recalés sur place,
        // sinon chaque lecture referait les visuels et se verrait à l'écran.
        runtime.Compose(scene, Transform(scene, "Caméra", x: 200, y: 120, width: 640, height: 360));
        composition.Refresh();

        Assert.Same(layer, Assert.Single(composition.Layers));
        Assert.Equal((200d, 120d), (layer.X, layer.Y));
    }

    [Fact]
    public void A_source_the_engine_does_not_compose_is_not_drawn()
    {
        var scene = SceneWith("Micro", "Masquée", "Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene,
            // Une source audio n'a aucune image, une source masquée n'est pas composée.
            Transform(scene, "Micro", x: 0, y: 0, width: 0, height: 0),
            Transform(scene, "Masquée", x: 0, y: 0, width: 1920, height: 1080, isVisible: false),
            Transform(scene, "Caméra", x: 0, y: 0, width: 1920, height: 1080));

        var composition = new SceneCompositionViewModel(runtime);
        composition.ShowScene(scene);

        Assert.Equal("Caméra", Assert.Single(composition.Layers).Name);
    }

    [Fact]
    public void A_composition_the_engine_refuses_is_reported_and_keeps_the_last_drawing()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 0, y: 0, width: 1920, height: 1080));

        var composition = new SceneCompositionViewModel(runtime);
        composition.ShowScene(scene);

        runtime.Result = _ => SceneCompositionResult.Failure("composition illisible");
        composition.Refresh();

        // Un incident passager ne doit pas effacer ce qui est dessiné, mais il doit se dire.
        Assert.Single(composition.Layers);
        Assert.Equal("composition illisible", composition.Status);
    }

    [Fact]
    public void An_unavailable_engine_leaves_an_empty_canvas_without_repeating_the_message()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 0, y: 0, width: 1920, height: 1080));

        var composition = new SceneCompositionViewModel(runtime);
        composition.ShowScene(scene);

        runtime.Result = _ => SceneCompositionResult.Unavailable("LibObs n'est pas connecté.");
        composition.Refresh();

        // L'indisponibilité du moteur est déjà annoncée par l'aperçu : rien de composé ici.
        Assert.Empty(composition.Layers);
        Assert.False(composition.HasLayers);
        Assert.Equal("", composition.Status);
    }

    [Fact]
    public void The_canvas_of_the_engine_wins_over_the_size_of_the_settings()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id) { CanvasWidth = 2560, CanvasHeight = 1440 };
        runtime.Compose(scene, Transform(scene, "Caméra", x: 0, y: 0, width: 2560, height: 1440));

        var composition = new SceneCompositionViewModel(runtime);
        composition.UseFallbackCanvas(1280, 720);
        Assert.Equal((1280d, 720d), (composition.CanvasWidth, composition.CanvasHeight));

        composition.ShowScene(scene);
        Assert.Equal((2560d, 1440d), (composition.CanvasWidth, composition.CanvasHeight));

        // Une fois le moteur lu, un repli venu des réglages ne reprend pas la main.
        composition.UseFallbackCanvas(1280, 720);
        Assert.Equal((2560d, 1440d), (composition.CanvasWidth, composition.CanvasHeight));
    }

    private static SceneItemViewModel SceneWith(params string[] sourceNames) =>
        new(new SceneDefinition
        {
            Name = "Composition",
            Sources = sourceNames.Select(name => new SourceDefinition { Name = name }).ToList()
        });

    private static SourceTransform Transform(
        SceneItemViewModel scene,
        string sourceName,
        double x,
        double y,
        double width,
        double height,
        SourceCrop crop = default,
        bool isVisible = true) =>
        new(
            scene.Sources.First(source => source.Name == sourceName).Id,
            x,
            y,
            width,
            height,
            1,
            1,
            crop,
            (int)width,
            (int)height,
            isVisible);

    /// <summary>
    /// Moteur simulé : il rend la composition qu'on lui pose, du premier plan vers
    /// l'arrière-plan, comme le fait <see cref="ISourceRuntime.GetSceneComposition"/>.
    /// </summary>
    private sealed class FakeCompositionRuntime(Guid sceneId) : ISourceRuntime
    {
        private SceneComposition _composition = SceneComposition.Empty;

        public int CanvasWidth { get; init; } = 1920;
        public int CanvasHeight { get; init; } = 1080;
        public Func<Guid, SceneCompositionResult>? Result { get; set; }

        public void Compose(SceneItemViewModel scene, params SourceTransform[] transforms)
        {
            Assert.Equal(sceneId, scene.Id);
            _composition = new SceneComposition(CanvasWidth, CanvasHeight, transforms);
        }

        public bool IsAvailable => true;
        public string UnavailableMessage => "";

        public SceneCompositionResult GetSceneComposition(Guid requestedSceneId) =>
            Result?.Invoke(requestedSceneId) ??
            (requestedSceneId == sceneId
                ? SceneCompositionResult.Success(_composition)
                : SceneCompositionResult.Failure("Cette scène n'existe pas dans LibObs."));

        public Task<SourceCatalog> EnumerateSourcesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SourceCatalog([], []));

        public SourceRuntimeResult AddSource(Guid scene, SourceAddRequest request) =>
            SourceRuntimeResult.Success(request.RequestedName);

        public SourceRuntimeResult RemoveSource(Guid scene, Guid source) => SourceRuntimeResult.Success();
        public SourceRuntimeResult SetMediaLoop(Guid scene, Guid source, bool loop) => SourceRuntimeResult.Success();
        public SourceOrderResult GetSourceOrder(Guid scene) => SourceOrderResult.Success([]);
        public SourceRuntimeResult MoveSource(Guid scene, Guid source, int layerIndex) => SourceRuntimeResult.Success();
    }
}
