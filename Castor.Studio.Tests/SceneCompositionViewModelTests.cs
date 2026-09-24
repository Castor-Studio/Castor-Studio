using CastorApplication.Models.Studio;
using CastorApplication.Services.Studio;
using CastorApplication.ViewModels.Scenes;

namespace Castor.Studio.Tests;

public sealed class SceneCompositionViewModelTests
{
    [Fact]
    public void Three_sources_keep_the_transform_the_engine_holds()
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

        // On peint de l'arrière vers l'avant : la liste rend l'empilement du moteur dans le
        // sens du dessin.
        Assert.Equal(
            [Id(scene, "Fond"), Id(scene, "Caméra"), Id(scene, "Overlay")],
            composition.Overlay.Sources.Select(source => source.Transform.SourceId));

        var camera = composition.Overlay.Sources[1].Transform;
        Assert.Equal((960d, 60d, 640d, 360d), (camera.X, camera.Y, camera.Width, camera.Height));
        Assert.Equal(new SourceCrop(40, 0, 40, 0), camera.Crop);
    }

    [Fact]
    public void Overlapping_sources_follow_the_stacking_order_of_the_engine()
    {
        var scene = SceneWith("Fond", "Overlay");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene,
            Transform(scene, "Overlay", x: 100, y: 100, width: 800, height: 600),
            Transform(scene, "Fond", x: 0, y: 0, width: 1920, height: 1080));

        var composition = new SceneCompositionViewModel(runtime);
        composition.ShowScene(scene);
        Assert.Equal([Id(scene, "Fond"), Id(scene, "Overlay")],
            composition.Overlay.Sources.Select(source => source.Transform.SourceId));

        // Le moteur remonte le fond devant : la composition doit suivre, pas conserver son
        // ordre.
        runtime.Compose(scene,
            Transform(scene, "Fond", x: 0, y: 0, width: 1920, height: 1080),
            Transform(scene, "Overlay", x: 100, y: 100, width: 800, height: 600));
        composition.Refresh();

        Assert.Equal([Id(scene, "Overlay"), Id(scene, "Fond")],
            composition.Overlay.Sources.Select(source => source.Transform.SourceId));
    }

    [Fact]
    public void Reopening_a_scene_takes_it_back_from_what_the_engine_holds_now()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 0, y: 0, width: 1920, height: 1080));

        var composition = new SceneCompositionViewModel(runtime);
        composition.ShowScene(scene);
        Assert.Equal(1920d, composition.Overlay.Sources[0].Transform.Width);

        // La transformation change du côté du moteur, sans que l'interface en soit prévenue.
        runtime.Compose(scene, Transform(scene, "Caméra", x: 320, y: 180, width: 1280, height: 720));

        composition.ShowScene(null);
        Assert.Empty(composition.Overlay.Sources);

        composition.ShowScene(scene);
        var source = Assert.Single(composition.Overlay.Sources);
        Assert.Equal((320d, 180d, 1280d, 720d), (source.Transform.X, source.Transform.Y, source.Transform.Width, source.Transform.Height));
    }

    [Fact]
    public void Each_reading_hands_over_a_list_that_no_longer_changes()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 0, y: 0, width: 640, height: 360));

        var composition = new SceneCompositionViewModel(runtime);
        composition.ShowScene(scene);
        var handedOver = composition.Overlay.Sources;

        runtime.Compose(scene, Transform(scene, "Caméra", x: 200, y: 120, width: 640, height: 360));
        composition.Refresh();

        // Le thread graphique du moteur relit la liste qu'on lui a donnée image par image :
        // la modifier sous lui la ferait lire pendant qu'elle change.
        Assert.NotSame(handedOver, composition.Overlay.Sources);
        Assert.Equal(0d, Assert.Single(handedOver).Transform.X);
        Assert.Equal(200d, Assert.Single(composition.Overlay.Sources).Transform.X);
    }

    [Fact]
    public void A_source_the_engine_does_not_compose_has_no_outline()
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

        Assert.Equal(Id(scene, "Caméra"), Assert.Single(composition.Overlay.Sources).Transform.SourceId);
    }

    [Fact]
    public void A_composition_the_engine_refuses_is_reported_and_keeps_the_last_reading()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 0, y: 0, width: 1920, height: 1080));

        var composition = new SceneCompositionViewModel(runtime);
        composition.ShowScene(scene);

        runtime.Result = _ => SceneCompositionResult.Failure("composition illisible");
        composition.Refresh();

        // Un incident passager ne doit pas effacer les cadres, mais il doit se dire.
        Assert.Single(composition.Overlay.Sources);
        Assert.Equal("composition illisible", composition.Status);
    }

    [Fact]
    public void An_unavailable_engine_composes_nothing_and_repeats_no_message()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 0, y: 0, width: 1920, height: 1080));

        var composition = new SceneCompositionViewModel(runtime);
        composition.ShowScene(scene);

        runtime.Result = _ => SceneCompositionResult.Unavailable("LibObs n'est pas connecté.");
        composition.Refresh();

        // L'indisponibilité du moteur est déjà annoncée par l'aperçu.
        Assert.Empty(composition.Overlay.Sources);
        Assert.Equal("", composition.Status);
    }

    [Fact]
    public void Each_source_carries_the_colour_the_list_gives_it()
    {
        var scene = SceneWith("Fond", "Caméra");
        scene.Sources[0].Color = "#34d399";
        scene.Sources[1].Color = "pas une couleur";
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene,
            Transform(scene, "Caméra", x: 0, y: 0, width: 640, height: 360),
            Transform(scene, "Fond", x: 0, y: 0, width: 1920, height: 1080));

        var composition = new SceneCompositionViewModel(runtime);
        composition.ShowScene(scene);

        // Sur une composition qui se chevauche, la couleur dit quel cadre est quelle ligne.
        Assert.Equal(new OverlayTint(0x34 / 255f, 0xd3 / 255f, 0x99 / 255f), composition.Overlay.Sources[0].Tint);

        // Une couleur illisible ne laisse pas une source sans cadre.
        Assert.Equal(OverlayTint.Default, composition.Overlay.Sources[1].Tint);
    }

    [Fact]
    public void Clicking_where_two_sources_overlap_takes_the_one_in_front()
    {
        var scene = SceneWith("Fond", "Overlay");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene,
            Transform(scene, "Overlay", x: 100, y: 100, width: 800, height: 600),
            Transform(scene, "Fond", x: 0, y: 0, width: 1920, height: 1080));

        var composition = new SceneCompositionViewModel(runtime);
        composition.ShowScene(scene);
        Assert.Null(composition.Selected);

        // Dans le recouvrement : c'est l'overlay que l'opérateur voit à cet endroit.
        composition.SelectAt(400, 300);
        Assert.Equal(Id(scene, "Overlay"), composition.Selected?.SourceId);
        Assert.Same(composition.Selected, composition.Overlay.Selected?.Transform);

        // Hors de l'overlay mais sur le fond.
        composition.SelectAt(1500, 900);
        Assert.Equal(Id(scene, "Fond"), composition.Selected?.SourceId);
    }

    [Fact]
    public void Clicking_beside_every_source_takes_none()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 100, y: 100, width: 200, height: 200));

        var composition = new SceneCompositionViewModel(runtime);
        composition.ShowScene(scene);

        composition.SelectAt(150, 150);
        Assert.NotNull(composition.Selected);

        composition.SelectAt(1000, 150);
        // Le cadre de la source reste : seuls ses points d'accroche s'en vont.
        Assert.Null(composition.Overlay.Selected);
        Assert.Single(composition.Overlay.Sources);
    }

    [Fact]
    public void The_selection_follows_the_source_the_engine_moves()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 0, y: 0, width: 640, height: 360));

        var composition = new SceneCompositionViewModel(runtime);
        composition.ShowScene(scene);
        composition.SelectAt(10, 10);

        // La sélection est tenue par identifiant : elle suit la source, elle ne reste pas
        // sur le rectangle où elle a été prise.
        runtime.Compose(scene, Transform(scene, "Caméra", x: 500, y: 400, width: 640, height: 360));
        composition.Refresh();

        Assert.Equal(Id(scene, "Caméra"), composition.Selected?.SourceId);
        Assert.Equal((500d, 400d), (composition.Selected!.X, composition.Selected.Y));
    }

    [Fact]
    public void A_source_that_stops_being_composed_drops_the_selection()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 0, y: 0, width: 640, height: 360));

        var composition = new SceneCompositionViewModel(runtime);
        composition.ShowScene(scene);
        composition.SelectAt(10, 10);

        runtime.Compose(scene, Transform(scene, "Caméra", x: 0, y: 0, width: 640, height: 360, isVisible: false));
        composition.Refresh();

        // Plus de cadre, donc plus de points d'accroche : les montrer sur une source que le
        // moteur ne compose plus laisserait saisir ce qui n'est pas là.
        Assert.Null(composition.Selected);
        Assert.Empty(composition.Overlay.Sources);
    }

    [Fact]
    public void Opening_another_scene_drops_the_selection()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 0, y: 0, width: 640, height: 360));

        var composition = new SceneCompositionViewModel(runtime);
        composition.ShowScene(scene);
        composition.SelectAt(10, 10);
        Assert.NotNull(composition.Selected);

        composition.ShowScene(null);
        composition.ShowScene(scene);

        Assert.Null(composition.Selected);
    }

    private static SceneItemViewModel SceneWith(params string[] sourceNames) =>
        new(new SceneDefinition
        {
            Name = "Composition",
            Sources = sourceNames.Select(name => new SourceDefinition { Name = name }).ToList()
        });

    private static Guid Id(SceneItemViewModel scene, string sourceName) =>
        scene.Sources.First(source => source.Name == sourceName).Id;

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
            Id(scene, sourceName),
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

        public Func<Guid, SceneCompositionResult>? Result { get; set; }

        public void Compose(SceneItemViewModel scene, params SourceTransform[] transforms)
        {
            Assert.Equal(sceneId, scene.Id);
            _composition = new SceneComposition(1920, 1080, transforms);
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
        public SourceRuntimeResult RenameSource(Guid scene, Guid source, string requestedName) => SourceRuntimeResult.Success(requestedName);
        public SourceRuntimeResult SetMediaLoop(Guid scene, Guid source, bool loop) => SourceRuntimeResult.Success();
        public SourceOrderResult GetSourceOrder(Guid scene) => SourceOrderResult.Success([]);
        public SourceRuntimeResult MoveSource(Guid scene, Guid source, int layerIndex) => SourceRuntimeResult.Success();
    }
}
