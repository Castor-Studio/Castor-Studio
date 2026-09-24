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

    [Fact]
    public void Dragging_a_source_moves_it_in_the_engine()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 0, y: 0, width: 640, height: 360));
        var composition = new SceneCompositionViewModel(runtime, new ManualTime());
        composition.ShowScene(scene);

        // Saisir une source la choisit et la déplace d'un même geste.
        var target = composition.BeginGesture(50, 50, HandleTolerance, crop: false);
        Assert.Equal(CompositionGestureKind.Move, target?.Kind);
        Assert.Equal(Id(scene, "Caméra"), composition.Selected?.SourceId);

        composition.UpdateGesture(150, 80, keepAspectRatio: true);
        composition.EndGesture();

        Assert.False(composition.IsGesturing);
        Assert.Equal((100d, 30d), (runtime.TransformOf(Id(scene, "Caméra")).X, runtime.TransformOf(Id(scene, "Caméra")).Y));
        Assert.Equal((100d, 30d), (composition.Selected!.X, composition.Selected.Y));
    }

    [Fact]
    public void The_outline_follows_the_pointer_between_two_writes_and_the_last_one_is_not_lost()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 0, y: 0, width: 640, height: 360));
        var time = new ManualTime();
        var composition = new SceneCompositionViewModel(runtime, time);
        composition.ShowScene(scene);

        composition.BeginGesture(50, 50, HandleTolerance, crop: false);
        composition.UpdateGesture(60, 50, keepAspectRatio: true);
        composition.UpdateGesture(70, 50, keepAspectRatio: true);
        composition.UpdateGesture(80, 50, keepAspectRatio: true);

        // Trois mouvements dans la même image : le moteur n'en reçoit qu'un, le cadre les
        // suit tous.
        Assert.Single(runtime.Writes);
        Assert.Equal(30d, composition.Selected!.X);

        // Le pointeur s'arrête : la dernière demande part quand l'intervalle est passé.
        composition.FlushGesture();
        Assert.Single(runtime.Writes);
        time.Advance(SceneCompositionViewModel.WriteInterval);
        composition.FlushGesture();
        Assert.Equal(30d, runtime.TransformOf(Id(scene, "Caméra")).X);

        // Et une demande encore en attente au relâcher est écrite, pas perdue.
        composition.UpdateGesture(90, 50, keepAspectRatio: true);
        composition.EndGesture();
        Assert.Equal(40d, runtime.TransformOf(Id(scene, "Caméra")).X);
    }

    [Fact]
    public void Pulling_a_handle_of_the_chosen_source_resizes_it_and_with_crop_crops_it()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 100, y: 100, width: 640, height: 360));
        var composition = new SceneCompositionViewModel(runtime, new ManualTime());
        composition.ShowScene(scene);
        composition.SelectAt(200, 200);

        // Le coin bas-droit, à (740, 460).
        var resize = composition.BeginGesture(742, 458, HandleTolerance, crop: false);
        Assert.Equal(new CompositionTarget(CompositionGestureKind.Resize, CompositionHandle.BottomRight), resize);
        composition.UpdateGesture(742 + 640, 458 + 360, keepAspectRatio: true);
        composition.EndGesture();

        var resized = runtime.TransformOf(Id(scene, "Caméra"));
        Assert.Equal((2d, 2d), (resized.ScaleX, resized.ScaleY));
        Assert.Equal((100d, 100d, 1280d, 720d), (resized.X, resized.Y, resized.Width, resized.Height));

        // Le côté gauche, rogné de 100 pixels du canvas : 50 pixels de la source à l'échelle 2.
        var crop = composition.BeginGesture(100, 460, HandleTolerance, crop: true);
        Assert.Equal(new CompositionTarget(CompositionGestureKind.Crop, CompositionHandle.Left), crop);
        composition.UpdateGesture(200, 460, keepAspectRatio: true);
        composition.EndGesture();

        var cropped = runtime.TransformOf(Id(scene, "Caméra"));
        Assert.Equal(new SourceCrop(50, 0, 0, 0), cropped.Crop);
        Assert.Equal((200d, 1380d), (cropped.X, cropped.X + cropped.Width));
    }

    [Fact]
    public void A_refused_write_puts_the_source_back_where_the_gesture_found_it()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 0, y: 0, width: 640, height: 360));
        var time = new ManualTime();
        var composition = new SceneCompositionViewModel(runtime, time);
        composition.ShowScene(scene);

        composition.BeginGesture(50, 50, HandleTolerance, crop: false);
        composition.UpdateGesture(150, 50, keepAspectRatio: true);
        Assert.Equal(100d, runtime.TransformOf(Id(scene, "Caméra")).X);

        // Le moteur refuse la suite du geste.
        runtime.Rejection = placement => placement.X == 200 ? "placement refusé" : null;
        time.Advance(SceneCompositionViewModel.WriteInterval);
        composition.UpdateGesture(250, 50, keepAspectRatio: true);

        // Le geste s'arrête net ; le moteur et l'écran repartent de ce qu'il y avait avant.
        Assert.False(composition.IsGesturing);
        Assert.Equal("placement refusé", composition.Status);
        Assert.Equal(new SourcePlacement(0, 0, 1, 1, SourceCrop.None), runtime.Writes[^1]);
        Assert.Equal(0d, runtime.TransformOf(Id(scene, "Caméra")).X);
        Assert.Equal(0d, composition.Selected!.X);

        // Les mouvements qui suivent le refus n'appartiennent plus à aucun geste.
        var writes = runtime.Writes.Count;
        composition.UpdateGesture(350, 50, keepAspectRatio: true);
        composition.EndGesture();
        Assert.Equal(writes, runtime.Writes.Count);
    }

    [Fact]
    public void When_the_engine_refuses_even_the_way_back_the_screen_shows_what_it_holds()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 0, y: 0, width: 640, height: 360));
        var time = new ManualTime();
        var composition = new SceneCompositionViewModel(runtime, time);
        composition.ShowScene(scene);

        composition.BeginGesture(50, 50, HandleTolerance, crop: false);
        composition.UpdateGesture(150, 50, keepAspectRatio: true);

        runtime.Rejection = _ => "moteur figé";
        time.Advance(SceneCompositionViewModel.WriteInterval);
        composition.UpdateGesture(250, 50, keepAspectRatio: true);

        // Ni la demande ni le retour ne sont passés : l'écran ne suit ni l'une ni l'autre,
        // il montre ce que le moteur détient réellement.
        Assert.False(composition.IsGesturing);
        Assert.Equal(100d, runtime.TransformOf(Id(scene, "Caméra")).X);
        Assert.Equal(100d, composition.Selected!.X);
        Assert.Equal("moteur figé", composition.Status);
    }

    [Fact]
    public void Cancelling_a_gesture_hands_the_source_back_its_placement()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 0, y: 0, width: 640, height: 360));
        var composition = new SceneCompositionViewModel(runtime, new ManualTime());
        composition.ShowScene(scene);

        composition.BeginGesture(50, 50, HandleTolerance, crop: false);
        composition.UpdateGesture(450, 350, keepAspectRatio: true);
        composition.CancelGesture();

        Assert.Equal((0d, 0d), (runtime.TransformOf(Id(scene, "Caméra")).X, runtime.TransformOf(Id(scene, "Caméra")).Y));
        Assert.Equal((0d, 0d), (composition.Selected!.X, composition.Selected.Y));
    }

    [Fact]
    public void A_refresh_during_a_gesture_does_not_pull_the_outline_back()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 0, y: 0, width: 640, height: 360));
        var composition = new SceneCompositionViewModel(runtime, new ManualTime());
        composition.ShowScene(scene);

        composition.BeginGesture(50, 50, HandleTolerance, crop: false);
        composition.UpdateGesture(60, 50, keepAspectRatio: true);
        composition.UpdateGesture(90, 50, keepAspectRatio: true);

        // La relecture périodique tombe entre deux écritures : le cadre reste sous le pointeur.
        composition.Refresh();
        Assert.Equal(40d, composition.Selected!.X);
    }

    [Fact]
    public void Three_sources_placed_by_hand_come_back_as_placed_when_the_scene_is_reopened()
    {
        var scene = SceneWith("Fond", "Caméra", "Logo");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene,
            Transform(scene, "Logo", x: 1600, y: 40, width: 200, height: 200),
            Transform(scene, "Caméra", x: 500, y: 300, width: 640, height: 360),
            Transform(scene, "Fond", x: 0, y: 0, width: 1920, height: 1080));
        var composition = new SceneCompositionViewModel(runtime, new ManualTime());
        composition.ShowScene(scene);

        // Le logo, déplacé.
        composition.BeginGesture(1700, 140, HandleTolerance, crop: false);
        composition.UpdateGesture(1600, 240, keepAspectRatio: true);
        composition.EndGesture();

        // La caméra, choisie puis réduite de moitié par son coin bas-droit.
        composition.SelectAt(600, 400);
        composition.BeginGesture(1140, 660, HandleTolerance, crop: false);
        composition.UpdateGesture(820, 480, keepAspectRatio: true);
        composition.EndGesture();

        // Le fond, choisi puis rogné par le haut.
        composition.SelectAt(50, 1000);
        composition.BeginGesture(960, 0, HandleTolerance, crop: true);
        composition.UpdateGesture(960, 60, keepAspectRatio: true);
        composition.EndGesture();

        composition.ShowScene(null);
        composition.ShowScene(scene);

        var placed = composition.Overlay.Sources.ToDictionary(source => source.Transform.SourceId, source => source.Transform);
        Assert.Equal((1500d, 140d), (placed[Id(scene, "Logo")].X, placed[Id(scene, "Logo")].Y));
        Assert.Equal((500d, 300d, 320d, 180d),
            (placed[Id(scene, "Caméra")].X, placed[Id(scene, "Caméra")].Y, placed[Id(scene, "Caméra")].Width, placed[Id(scene, "Caméra")].Height));
        Assert.Equal(new SourceCrop(0, 60, 0, 0), placed[Id(scene, "Fond")].Crop);
        Assert.Equal(60d, placed[Id(scene, "Fond")].Y);
    }

    [Fact]
    public void Pressing_beside_every_source_starts_no_gesture_and_takes_none()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 0, y: 0, width: 640, height: 360));
        var composition = new SceneCompositionViewModel(runtime, new ManualTime());
        composition.ShowScene(scene);
        composition.SelectAt(10, 10);

        Assert.Null(composition.BeginGesture(1500, 900, HandleTolerance, crop: false));
        Assert.False(composition.IsGesturing);
        Assert.Null(composition.Selected);
        Assert.Empty(runtime.Writes);
    }

    private const double HandleTolerance = 6;

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

        /// <summary>Chaque placement que le moteur a reçu, dans l'ordre, refusés compris.</summary>
        public List<SourcePlacement> Writes { get; } = [];

        /// <summary>Posé, le moteur refuse les placements pour lesquels il rend un message.</summary>
        public Func<SourcePlacement, string?>? Rejection { get; set; }

        public SourceTransform TransformOf(Guid sourceId) =>
            _composition.Sources.Single(source => source.SourceId == sourceId);

        // Le moteur applique le placement et recompose le rectangle à sa règle : taille de la
        // source, moins le rognage, mise à l'échelle.
        public SourceTransformResult SetSourceTransform(Guid scene, Guid source, SourcePlacement placement)
        {
            Writes.Add(placement);
            if (Rejection?.Invoke(placement) is { } refusal) return SourceTransformResult.Failure(refusal);
            if (scene != sceneId) return SourceTransformResult.Failure("Cette scène n'existe pas dans LibObs.");

            var current = TransformOf(source);
            var written = current with
            {
                X = placement.X,
                Y = placement.Y,
                Width = (current.SourceWidth - placement.Crop.Left - placement.Crop.Right) * placement.ScaleX,
                Height = (current.SourceHeight - placement.Crop.Top - placement.Crop.Bottom) * placement.ScaleY,
                ScaleX = placement.ScaleX,
                ScaleY = placement.ScaleY,
                Crop = placement.Crop
            };
            _composition = _composition with
            {
                Sources = _composition.Sources.Select(item => item.SourceId == source ? written : item).ToArray()
            };
            return SourceTransformResult.Success(written);
        }
    }

    /// <summary>Une horloge qui n'avance que quand le test le dit.</summary>
    private sealed class ManualTime : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(TimeSpan span) => _ticks += span.Ticks;
    }
}
