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
        // Foreground first, as everywhere else in the application.
        runtime.Compose(scene,
            Transform(scene, "Overlay", x: 1400, y: 820, width: 480, height: 240),
            Transform(scene, "Caméra", x: 960, y: 60, width: 640, height: 360,
                crop: new SourceCrop(40, 0, 40, 0)),
            Transform(scene, "Fond", x: 0, y: 0, width: 1920, height: 1080));

        var composition = new SceneCompositionViewModel(runtime);
        composition.ShowScene(scene);

        // Painting goes from back to front: the list gives the engine's stack in drawing order.
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

        // The engine brings the background forward: the composition must follow, not keep its order.
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

        // The transform changes on the engine side, without the interface being told.
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

        // The engine's graphics thread rereads the list it was given every frame: changing it
        // underneath would have it read while the list changes.
        Assert.NotSame(handedOver, composition.Overlay.Sources);
        Assert.Equal(0d, Assert.Single(handedOver).Transform.X);
        Assert.Equal(200d, Assert.Single(composition.Overlay.Sources).Transform.X);
    }

    [Fact]
    public void A_source_the_engine_does_not_compose_has_no_outline()
    {
        var scene = SceneWith("Micro", "Masquée", "Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        // An audio source has no image, a hidden source is not composed.
        runtime.Compose(scene,
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

        // A passing incident must not erase the frames, but it must be reported.
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

        // The engine being unavailable is already announced by the preview.
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

        // On an overlapping composition, color tells which frame is which row.
        Assert.Equal(new OverlayTint(0x34 / 255f, 0xd3 / 255f, 0x99 / 255f), composition.Overlay.Sources[0].Tint);

        // An unreadable color does not leave a source without a frame.
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

        // In the overlap: the overlay is what the operator sees at that spot.
        composition.SelectAt(400, 300);
        Assert.Equal(Id(scene, "Overlay"), composition.Selected?.SourceId);
        Assert.Same(composition.Selected, composition.Overlay.Selected?.Transform);

        // Outside the overlay but on the background.
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
        // The source frame stays: only its grab points go away.
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

        // Selection is held by id: it follows the source, it does not stay on the rectangle where
        // it was taken.
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

        // No frame, so no grab points: showing them on a source the engine no longer composes
        // would let the operator grab what is not there.
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

        // Grabbing a source selects and moves it in one gesture.
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

        // Three moves within one frame: the engine gets one, the outline follows them all.
        Assert.Single(runtime.Writes);
        Assert.Equal(30d, composition.Selected!.X);

        // The pointer stops: the last request goes out once the interval has passed.
        composition.FlushGesture();
        Assert.Single(runtime.Writes);
        time.Advance(SceneCompositionViewModel.WriteInterval);
        composition.FlushGesture();
        Assert.Equal(30d, runtime.TransformOf(Id(scene, "Caméra")).X);

        // And a request still pending on release is written, not lost.
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

        // The bottom-right corner, at (740, 460).
        var resize = composition.BeginGesture(742, 458, HandleTolerance, crop: false);
        Assert.Equal(new CompositionTarget(CompositionGestureKind.Resize, CompositionHandle.BottomRight), resize);
        composition.UpdateGesture(742 + 640, 458 + 360, keepAspectRatio: true);
        composition.EndGesture();

        var resized = runtime.TransformOf(Id(scene, "Caméra"));
        Assert.Equal((2d, 2d), (resized.ScaleX, resized.ScaleY));
        Assert.Equal((100d, 100d, 1280d, 720d), (resized.X, resized.Y, resized.Width, resized.Height));

        // The left side, cropped by 100 canvas pixels: 50 source pixels at scale 2.
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

        // The engine refuses the rest of the gesture.
        runtime.Rejection = placement => placement.X == 200 ? "placement refusé" : null;
        time.Advance(SceneCompositionViewModel.WriteInterval);
        composition.UpdateGesture(250, 50, keepAspectRatio: true);

        // The gesture stops dead; engine and screen go back to what was there before.
        Assert.False(composition.IsGesturing);
        Assert.Equal("placement refusé", composition.Status);
        Assert.Equal(new SourcePlacement(0, 0, 1, 1, SourceCrop.None), runtime.Writes[^1]);
        Assert.Equal(0d, runtime.TransformOf(Id(scene, "Caméra")).X);
        Assert.Equal(0d, composition.Selected!.X);

        // Moves after the refusal no longer belong to any gesture.
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

        // Neither the request nor the restore went through: the screen follows neither, it shows
        // what the engine really holds.
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

        // The periodic read lands between two writes: the frame stays under the pointer.
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

        // The logo, moved.
        composition.BeginGesture(1700, 140, HandleTolerance, crop: false);
        composition.UpdateGesture(1600, 240, keepAspectRatio: true);
        composition.EndGesture();

        // The camera, selected then halved by its bottom-right corner.
        composition.SelectAt(600, 400);
        composition.BeginGesture(1140, 660, HandleTolerance, crop: false);
        composition.UpdateGesture(820, 480, keepAspectRatio: true);
        composition.EndGesture();

        // The background, selected then cropped from the top.
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

    [Fact]
    public void Pulling_just_outside_a_corner_turns_the_source_around_its_centre()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 100, y: 100, width: 640, height: 360));
        var composition = new SceneCompositionViewModel(runtime, new ManualTime());
        composition.ShowScene(scene);
        composition.SelectAt(200, 200);

        // Just past the bottom-right corner (740, 460), outside the source and its handle.
        var target = composition.BeginGesture(752, 472, HandleTolerance, crop: false, rotationReach: RotationReach);
        Assert.Equal(new CompositionTarget(CompositionGestureKind.Rotate, CompositionHandle.BottomRight), target);

        // The center is at (420, 280): going from the bottom-right corner to the symmetric
        // bottom-left one is a little over a quarter turn; Shift brings it to the 15° step.
        composition.UpdateGesture(88, 472, keepAspectRatio: true, constrainRotation: true);
        Assert.Equal("120°", composition.GestureReadout);
        composition.EndGesture();

        var turned = runtime.TransformOf(Id(scene, "Caméra"));
        Assert.Equal(120, turned.Rotation, 6);
        var (centreX, centreY) = CompositionGeometry.Center(turned);
        Assert.Equal(420, centreX, 6);
        Assert.Equal(280, centreY, 6);
    }

    [Fact]
    public void An_engine_that_cannot_turn_sources_leaves_them_straight_and_says_so()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id)
        {
            // What LibObs 0.3.0 does, as it does not expose rotation.
            Rejection = placement => placement.Rotation != 0 ? "rotation indisponible" : null
        };
        runtime.Compose(scene, Transform(scene, "Caméra", x: 100, y: 100, width: 640, height: 360));
        var composition = new SceneCompositionViewModel(runtime, new ManualTime());
        composition.ShowScene(scene);
        composition.SelectAt(200, 200);

        composition.BeginGesture(752, 472, HandleTolerance, crop: false, rotationReach: RotationReach);
        composition.UpdateGesture(700, 520, keepAspectRatio: true);

        // The first write is refused: the gesture stops, nothing has turned, on screen or in the
        // engine.
        Assert.False(composition.IsGesturing);
        Assert.Equal(0d, composition.Selected!.Rotation);
        Assert.Equal("rotation indisponible", composition.Status);

        composition.RotateSelected(90);
        Assert.Equal(0d, runtime.TransformOf(Id(scene, "Caméra")).Rotation);
        Assert.Equal("rotation indisponible", composition.Status);
    }

    [Fact]
    public void Quarter_turns_from_the_menu_go_through_the_engine()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 100, y: 100, width: 640, height: 360));
        var composition = new SceneCompositionViewModel(runtime, new ManualTime());
        composition.ShowScene(scene);
        composition.SelectAt(200, 200);

        composition.RotateSelected(90);
        composition.RotateSelected(90);
        Assert.Equal(180, runtime.TransformOf(Id(scene, "Caméra")).Rotation, 6);
        Assert.Equal(180, composition.Selected!.Rotation, 6);

        composition.ResetSelectedRotation();
        Assert.Equal(0, runtime.TransformOf(Id(scene, "Caméra")).Rotation, 6);
        Assert.Equal((100, 100), (Math.Round(composition.Selected.X), Math.Round(composition.Selected.Y)));
    }

    [Fact]
    public void In_crop_mode_the_handles_crop_and_the_corners_no_longer_turn()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 100, y: 100, width: 640, height: 360));
        var composition = new SceneCompositionViewModel(runtime, new ManualTime());
        composition.ShowScene(scene);

        // Nothing to crop while no source is selected.
        composition.EnterCropMode();
        Assert.False(composition.IsCropping);

        composition.SelectAt(200, 200);
        composition.EnterCropMode();
        Assert.True(composition.IsCropping);
        Assert.True(composition.Overlay.IsCropping);

        Assert.Equal(new CompositionTarget(CompositionGestureKind.Crop, CompositionHandle.Left),
            composition.TargetAt(100, 280, HandleTolerance, crop: false, rotationReach: RotationReach));
        Assert.Equal(new CompositionTarget(CompositionGestureKind.Move, null),
            composition.TargetAt(300, 300, HandleTolerance, crop: false, rotationReach: RotationReach));
        Assert.Null(composition.TargetAt(752, 472, HandleTolerance, crop: false, rotationReach: RotationReach));

        composition.BeginGesture(100, 280, HandleTolerance, crop: false, rotationReach: RotationReach);
        composition.UpdateGesture(140, 280, keepAspectRatio: true);
        Assert.Equal("Gauche 40 · Haut 0 · Droite 0 · Bas 0", composition.GestureReadout);
        composition.EndGesture();

        // The mode holds from one gesture to the next.
        Assert.True(composition.IsCropMode);
        Assert.Equal(new SourceCrop(40, 0, 0, 0), runtime.TransformOf(Id(scene, "Caméra")).Crop);

        composition.ExitCropMode();
        Assert.False(composition.IsCropping);
    }

    [Fact]
    public void Choosing_another_source_or_none_ends_the_crop_mode()
    {
        var scene = SceneWith("Fond", "Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene,
            Transform(scene, "Caméra", x: 100, y: 100, width: 640, height: 360),
            Transform(scene, "Fond", x: 0, y: 0, width: 1920, height: 1080));
        var composition = new SceneCompositionViewModel(runtime, new ManualTime());
        composition.ShowScene(scene);

        composition.SelectAt(200, 200);
        composition.EnterCropMode();
        composition.BeginGesture(1500, 900, HandleTolerance, crop: false);
        composition.EndGesture();
        Assert.Equal(Id(scene, "Fond"), composition.Selected?.SourceId);
        Assert.False(composition.IsCropMode);

        composition.SelectAt(200, 200);
        composition.ToggleCropMode();
        Assert.True(composition.IsCropMode);
        composition.ClearSelection();
        Assert.False(composition.IsCropMode);
    }

    [Fact]
    public void Holding_alt_shows_the_crop_look_only_while_it_is_held()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 100, y: 100, width: 640, height: 360));
        var composition = new SceneCompositionViewModel(runtime, new ManualTime());
        composition.ShowScene(scene);
        composition.SelectAt(200, 200);

        composition.SetCropModifier(true);
        Assert.True(composition.Overlay.IsCropping);
        Assert.False(composition.IsCropMode);

        composition.SetCropModifier(false);
        Assert.False(composition.Overlay.IsCropping);
    }

    [Fact]
    public void Resetting_the_crop_from_the_menu_gives_the_whole_source_back_in_place()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        // 640 source pixels, 40 cropped on the left: 600 composed, starting at x = 140.
        var cropped = Transform(scene, "Caméra", x: 140, y: 100, width: 600, height: 360,
            crop: new SourceCrop(40, 0, 0, 0));
        runtime.Compose(scene, cropped with { SourceWidth = 640 });
        var composition = new SceneCompositionViewModel(runtime, new ManualTime());
        composition.ShowScene(scene);
        composition.SelectAt(200, 200);

        composition.ResetSelectedCrop();

        var whole = runtime.TransformOf(Id(scene, "Caméra"));
        Assert.Equal(SourceCrop.None, whole.Crop);
        Assert.Equal((100d, 640d), (whole.X, whole.Width));
    }

    [Fact]
    public void Zooming_from_the_menu_keeps_the_aimed_point_and_leaves_the_frame_alone()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        var camera = Transform(scene, "Caméra", x: 100, y: 100, width: 640, height: 360);
        runtime.Compose(scene, camera with { Zoom = new SourceZoom(2, 0.25, 0.75) });
        var composition = new SceneCompositionViewModel(runtime, new ManualTime());
        composition.ShowScene(scene);
        composition.SelectAt(200, 200);

        composition.ZoomSelected(3);

        Assert.Equal(new SourceZoom(3, 0.25, 0.75), composition.Selected!.Zoom);
        Assert.Equal(camera.Placement, composition.Selected.Placement);

        composition.ResetSelectedZoom();

        Assert.Equal(SourceZoom.None, runtime.TransformOf(Id(scene, "Caméra")).Zoom);
        Assert.Empty(runtime.Writes);
    }

    [Fact]
    public void With_alt_dragging_inside_a_zoomed_source_slides_its_image_and_leaves_the_frame_alone()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        var camera = Transform(scene, "Caméra", x: 100, y: 100, width: 640, height: 360);
        runtime.Compose(scene, camera with { Zoom = new SourceZoom(2) });
        var composition = new SceneCompositionViewModel(runtime, new ManualTime());
        composition.ShowScene(scene);
        composition.SelectAt(200, 200);

        // Without Alt, dragging moves the frame, as always.
        Assert.Equal(CompositionGestureKind.Move, composition.TargetAt(420, 280, HandleTolerance, crop: false)?.Kind);

        // Pulling the image 160 px left is a quarter of the frame, so an eighth of the image at
        // ×2: the aimed point moves right.
        var target = composition.BeginGesture(420, 280, HandleTolerance, crop: true);
        Assert.Equal(CompositionGestureKind.Pan, target?.Kind);
        composition.UpdateGesture(260, 280, keepAspectRatio: true);
        composition.EndGesture();

        var panned = runtime.TransformOf(Id(scene, "Caméra"));
        Assert.Equal(0.625, panned.Zoom.CenterX, 6);
        Assert.Equal(0.5, panned.Zoom.CenterY, 6);
        Assert.Equal(camera.Placement, panned.Placement);
        Assert.Empty(runtime.Writes);
    }

    [Fact]
    public void A_click_on_the_zoom_map_jumps_there_and_a_drag_keeps_aiming()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        var camera = Transform(scene, "Caméra", x: 100, y: 100, width: 1280, height: 720);
        runtime.Compose(scene, camera with { Zoom = new SourceZoom(2) });
        var composition = new SceneCompositionViewModel(runtime, new ManualTime());
        composition.ShowScene(scene);
        composition.SelectAt(200, 200);

        // At scale 1, the map is 120 × 67.5 in the bottom-right corner, 8 px from the edges.
        var map = ZoomMap.Bounds(1280, 720, 1)!.Value;
        double CanvasX(double share) => 100 + map.X + map.Width * share;
        double CanvasY(double share) => 100 + map.Y + map.Height * share;

        // Without Alt: it is a visible control, it can be grabbed as is.
        var target = composition.BeginGesture(CanvasX(0.7), CanvasY(0.5), HandleTolerance, crop: false,
            canvasPerScreenPixel: 1);
        Assert.Equal(CompositionGestureKind.Aim, target?.Kind);
        Assert.Equal(0.7, Assert.Single(runtime.ZoomWrites).CenterX, 6);

        // Aiming past the edge pins the window against it.
        composition.UpdateGesture(CanvasX(1.2), CanvasY(0.5), keepAspectRatio: true);
        composition.EndGesture();

        var aimed = runtime.TransformOf(Id(scene, "Caméra"));
        Assert.Equal(0.75, aimed.Zoom.CenterX, 6);
        Assert.Equal(camera.Placement, aimed.Placement);
    }

    [Fact]
    public void The_keyboard_zooms_nudges_and_resets_the_chosen_source()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 100, y: 100, width: 640, height: 360));
        var composition = new SceneCompositionViewModel(runtime, new ManualTime());
        composition.ShowScene(scene);
        composition.SelectAt(200, 200);

        // Without zoom, an arrow has nothing to slide.
        composition.NudgeSelectedZoom(1, 0);
        Assert.Empty(runtime.ZoomWrites);

        for (var step = 0; step < 5; step++) composition.StepSelectedZoom(1);
        var zoomed = composition.Selected!.Zoom;
        Assert.Equal(Math.Pow(1.15, 5), zoomed.Factor, 6);

        // One step is a tenth of what the frame shows.
        composition.NudgeSelectedZoom(1, -1);
        Assert.Equal(0.5 + 0.1 / zoomed.Factor, composition.Selected.Zoom.CenterX, 6);
        Assert.Equal(0.5 - 0.1 / zoomed.Factor, composition.Selected.Zoom.CenterY, 6);

        composition.ResetSelectedZoom();
        Assert.Equal(SourceZoom.None, composition.Selected.Zoom);
    }

    [Fact]
    public void Alt_and_the_wheel_zoom_towards_the_pointer_and_the_wheel_alone_does_nothing()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 100, y: 100, width: 640, height: 360));
        var composition = new SceneCompositionViewModel(runtime, new ManualTime());
        composition.ShowScene(scene);

        // The pointer at mid-height near the frame's right edge, three quarters across its width.
        Assert.False(composition.ZoomAt(580, 280, 5));
        Assert.Empty(runtime.ZoomWrites);

        composition.SetCropModifier(true);
        Assert.True(composition.ZoomAt(580, 280, 5));

        // The wheel selected the source, and the image point under the pointer has not moved.
        var zoom = runtime.TransformOf(Id(scene, "Caméra")).Zoom;
        Assert.Equal(Id(scene, "Caméra"), composition.Selected?.SourceId);
        Assert.True(zoom.IsZoomed);
        Assert.Equal(0.75, zoom.CenterX - 0.5 / zoom.Factor + 0.75 / zoom.Factor, 6);
        Assert.Equal(0.5, zoom.CenterY, 6);
    }

    [Fact]
    public void A_dragged_source_snaps_to_the_centre_and_shows_its_guides_until_released()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 0, y: 0, width: 640, height: 360));
        var composition = new SceneCompositionViewModel(runtime, new ManualTime());
        composition.ShowScene(scene);

        // Centered in the 1920×1080 canvas, the camera's origin is at (640, 360); the pointer
        // brings it within 5 pixels.
        composition.BeginGesture(10, 10, HandleTolerance, crop: false);
        composition.UpdateGesture(645, 365, keepAspectRatio: true, snapReach: SnapReach);

        Assert.Equal((640d, 360d), (composition.Selected!.X, composition.Selected.Y));
        Assert.Contains(new CompositionGuide(IsVertical: true, 960), composition.Overlay.Guides!);
        Assert.Contains(new CompositionGuide(IsVertical: false, 540), composition.Overlay.Guides!);

        composition.EndGesture();
        Assert.Equal((640d, 360d), (runtime.TransformOf(Id(scene, "Caméra")).X, runtime.TransformOf(Id(scene, "Caméra")).Y));
        Assert.Empty(composition.Overlay.Guides!);
    }

    [Fact]
    public void Without_a_snapping_reach_the_source_goes_exactly_where_the_pointer_puts_it()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 0, y: 0, width: 640, height: 360));
        var composition = new SceneCompositionViewModel(runtime, new ManualTime());
        composition.ShowScene(scene);

        composition.BeginGesture(10, 10, HandleTolerance, crop: false);
        composition.UpdateGesture(645, 365, keepAspectRatio: true);

        Assert.Equal((635d, 355d), (composition.Selected!.X, composition.Selected.Y));
        Assert.Empty(composition.Overlay.Guides!);
    }

    [Fact]
    public void The_hovered_source_is_shown_until_the_pointer_leaves_or_a_gesture_starts()
    {
        var scene = SceneWith("Fond", "Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene,
            Transform(scene, "Caméra", x: 100, y: 100, width: 640, height: 360),
            Transform(scene, "Fond", x: 0, y: 0, width: 1920, height: 1080));
        var composition = new SceneCompositionViewModel(runtime, new ManualTime());
        composition.ShowScene(scene);

        Assert.True(composition.HoverAt(200, 200));
        Assert.Equal(Id(scene, "Caméra"), composition.Overlay.Hovered?.Transform.SourceId);
        // The same hover gives the engine nothing new.
        Assert.False(composition.HoverAt(210, 210));

        // During a gesture, only the grabbed source counts.
        composition.BeginGesture(1500, 900, HandleTolerance, crop: false);
        Assert.Null(composition.Overlay.Hovered);
        composition.EndGesture();

        // The selected source already has that weight: it does not show as hovered.
        composition.HoverAt(1500, 900);
        Assert.Null(composition.Overlay.Hovered);

        composition.HoverAt(200, 200);
        Assert.True(composition.ClearHover());
        Assert.Null(composition.Overlay.Hovered);
    }

    [Fact]
    public void Tab_walks_the_stack_from_the_front_and_lets_go_past_the_last_source()
    {
        var scene = SceneWith("Fond", "Caméra", "Overlay");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene,
            Transform(scene, "Overlay", x: 1400, y: 820, width: 480, height: 240),
            Transform(scene, "Caméra", x: 960, y: 60, width: 640, height: 360),
            Transform(scene, "Fond", x: 0, y: 0, width: 1920, height: 1080));
        var composition = new SceneCompositionViewModel(runtime);
        composition.ShowScene(scene);

        // In source list order: foreground first.
        Assert.True(composition.SelectNext(1));
        Assert.Equal(Id(scene, "Overlay"), composition.Selected?.SourceId);
        Assert.True(composition.SelectNext(1));
        Assert.Equal(Id(scene, "Caméra"), composition.Selected?.SourceId);
        Assert.True(composition.SelectNext(1));
        Assert.Equal(Id(scene, "Fond"), composition.Selected?.SourceId);

        // Past the last one, nothing is selected: Tab can leave the preview.
        Assert.False(composition.SelectNext(1));
        Assert.False(composition.HasSelection);

        // Going backwards with no selection starts from the background.
        Assert.True(composition.SelectNext(-1));
        Assert.Equal(Id(scene, "Fond"), composition.Selected?.SourceId);
    }

    [Fact]
    public void Arrows_push_the_selected_source_by_canvas_pixels()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 100, y: 100, width: 640, height: 360));
        var composition = new SceneCompositionViewModel(runtime);
        composition.ShowScene(scene);
        composition.SelectAt(200, 200);

        composition.NudgeSelected(-10, 1);

        Assert.Equal((90d, 101d), (composition.Selected!.X, composition.Selected.Y));
    }

    [Fact]
    public void Center_and_fit_place_the_selected_source_in_the_canvas()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 0, y: 0, width: 640, height: 360));
        var composition = new SceneCompositionViewModel(runtime);
        composition.ShowScene(scene);
        composition.SelectAt(10, 10);

        composition.CenterSelected();
        Assert.Equal((640d, 360d), (composition.Selected!.X, composition.Selected.Y));

        // 640 × 360 fits three times in 1920 × 1080: the source fills it.
        composition.FitSelected();
        var fitted = composition.Selected!;
        Assert.Equal((0d, 0d, 1920d, 1080d), (fitted.X, fitted.Y, fitted.Width, fitted.Height));
    }

    [Fact]
    public void Reset_puts_the_selected_source_back_to_its_original_placement()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 300, y: 200, width: 640, height: 360,
            crop: new SourceCrop(20, 0, 20, 0)));
        var composition = new SceneCompositionViewModel(runtime);
        composition.ShowScene(scene);
        composition.SelectAt(400, 300);
        composition.RotateSelected(90);
        composition.ZoomSelected(2);

        composition.ResetSelectedTransform();

        var reset = composition.Selected!;
        Assert.Equal(new SourcePlacement(0, 0, 1, 1, SourceCrop.None), reset.Placement);
        Assert.False(reset.Zoom.IsZoomed);
    }

    [Fact]
    public void Arranging_asks_the_source_list_for_the_selected_source()
    {
        var scene = SceneWith("Fond", "Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene,
            Transform(scene, "Caméra", x: 960, y: 60, width: 640, height: 360),
            Transform(scene, "Fond", x: 0, y: 0, width: 1920, height: 1080));
        var requests = new List<(Guid, SourceArrangement)>();
        var composition = new SceneCompositionViewModel(runtime, arrange: (id, arrangement) => requests.Add((id, arrangement)));
        composition.ShowScene(scene);

        // With no selected source, nothing is requested.
        composition.ArrangeSelected(SourceArrangement.ToFront);
        Assert.Empty(requests);

        composition.SelectAt(100, 900);
        composition.ArrangeSelected(SourceArrangement.ToFront);
        Assert.Equal([(Id(scene, "Fond"), SourceArrangement.ToFront)], requests);
    }

    [Fact]
    public void The_toolbar_hears_of_a_selection_only_when_it_changes()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 0, y: 0, width: 640, height: 360));
        var composition = new SceneCompositionViewModel(runtime);
        composition.ShowScene(scene);
        var changes = new List<string?>();
        composition.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        composition.SelectAt(10, 10);
        composition.Refresh();
        composition.EnterCropMode();
        composition.ClearSelection();

        Assert.Equal(
            [nameof(SceneCompositionViewModel.HasSelection), nameof(SceneCompositionViewModel.IsCropMode),
             nameof(SceneCompositionViewModel.HasSelection), nameof(SceneCompositionViewModel.IsCropMode)],
            changes.Where(name => name is nameof(SceneCompositionViewModel.HasSelection) or nameof(SceneCompositionViewModel.IsCropMode)));
    }

    [Fact]
    public void The_zoom_slider_writes_to_the_engine_and_follows_zoom_given_elsewhere()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 0, y: 0, width: 640, height: 360));
        var composition = new SceneCompositionViewModel(runtime);
        composition.ShowScene(scene);
        var changes = new List<string?>();
        composition.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        // With no source, the slider sits at ×1 and writes nothing.
        composition.SelectedZoomFactor = 2;
        Assert.Empty(runtime.ZoomWrites);
        Assert.Equal(1d, composition.SelectedZoomFactor);

        composition.SelectAt(10, 10);
        composition.SelectedZoomFactor = 2;
        Assert.Equal(2d, composition.SelectedZoomFactor);
        Assert.Contains(nameof(SceneCompositionViewModel.SelectedZoomFactor), changes);

        // The slider sends back the value it was just given: nothing more is written.
        composition.SelectedZoomFactor = 2;
        Assert.Single(runtime.ZoomWrites);

        // A zoom given from the keyboard makes it follow.
        changes.Clear();
        composition.ResetSelectedZoom();
        Assert.Equal(1d, composition.SelectedZoomFactor);
        Assert.Contains(nameof(SceneCompositionViewModel.SelectedZoomFactor), changes);
    }

    [Fact]
    public void Choosing_from_the_list_takes_a_composed_source_and_drops_one_that_is_not()
    {
        var scene = SceneWith("Caméra", "Micro");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene,
            Transform(scene, "Caméra", x: 0, y: 0, width: 640, height: 360),
            Transform(scene, "Micro", x: 0, y: 0, width: 0, height: 0));
        var composition = new SceneCompositionViewModel(runtime);
        composition.ShowScene(scene);
        var changes = 0;
        composition.OverlayChanged += (_, _) => changes++;

        Assert.True(composition.Select(Id(scene, "Caméra")));
        Assert.Equal(Id(scene, "Caméra"), composition.Selected?.SourceId);
        Assert.True(changes > 0);

        // An audio source has no frame: nothing is selected, not even the previous source.
        Assert.False(composition.Select(Id(scene, "Micro")));
        Assert.False(composition.HasSelection);
    }

    [Fact]
    public void A_command_needs_a_chosen_source_except_to_choose_one()
    {
        var scene = SceneWith("Caméra");
        var runtime = new FakeCompositionRuntime(scene.Id);
        runtime.Compose(scene, Transform(scene, "Caméra", x: 0, y: 0, width: 640, height: 360));
        var composition = new SceneCompositionViewModel(runtime);
        composition.ShowScene(scene);

        Assert.False(composition.Execute(new CompositionKeyAction(CompositionCommand.CenterSelected)));
        Assert.Empty(runtime.Writes);

        Assert.True(composition.Execute(new CompositionKeyAction(CompositionCommand.SelectNext)));
        Assert.True(composition.Execute(new CompositionKeyAction(CompositionCommand.CenterSelected)));
        Assert.Equal((640d, 360d), (composition.Selected!.X, composition.Selected.Y));
    }

    private const double HandleTolerance = 6;
    private const double RotationReach = 24;
    private const double SnapReach = 10;

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
    /// Simulated engine: it returns the composition it was given, from foreground to background,
    /// as <see cref="ISourceRuntime.GetSceneComposition"/> does.
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

        /// <summary>Every placement the engine received, in order, refused ones included.</summary>
        public List<SourcePlacement> Writes { get; } = [];

        /// <summary>When set, the engine refuses the placements it returns a message for.</summary>
        public Func<SourcePlacement, string?>? Rejection { get; set; }

        public SourceTransform TransformOf(Guid sourceId) =>
            _composition.Sources.Single(source => source.SourceId == sourceId);

        // The engine applies the placement and recomposes the rectangle by its rule: source size,
        // minus the crop, scaled.
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
                Crop = placement.Crop,
                Rotation = placement.Rotation
            };
            _composition = _composition with
            {
                Sources = _composition.Sources.Select(item => item.SourceId == source ? written : item).ToArray()
            };
            return SourceTransformResult.Success(written);
        }

        /// <summary>Every zoom the engine received, in order.</summary>
        public List<SourceZoom> ZoomWrites { get; } = [];

        // Zoom happens inside the frame: the engine changes only that.
        public SourceTransformResult SetSourceZoom(Guid scene, Guid source, SourceZoom zoom)
        {
            ZoomWrites.Add(zoom);
            if (scene != sceneId) return SourceTransformResult.Failure("Cette scène n'existe pas dans LibObs.");

            var written = TransformOf(source) with { Zoom = zoom };
            _composition = _composition with
            {
                Sources = _composition.Sources.Select(item => item.SourceId == source ? written : item).ToArray()
            };
            return SourceTransformResult.Success(written);
        }
    }

    /// <summary>A clock that only moves when the test says so.</summary>
    private sealed class ManualTime : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(TimeSpan span) => _ticks += span.Ticks;
    }
}
