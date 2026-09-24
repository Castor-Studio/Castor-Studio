using CastorApplication.Models.Settings;
using CastorApplication.Models.Studio;
using CastorApplication.Services;
using CastorApplication.Services.Dialogs;
using CastorApplication.Services.Settings;
using CastorApplication.Services.Platform;
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
        Assert.Equal("échec natif", viewModel.CreateSceneError);

        viewModel.NewSceneName = "Refusée bis";
        Assert.Equal("", viewModel.CreateSceneError);

        viewModel.NewSceneName = "   ";
        Assert.False(viewModel.CreateSceneCommand.CanExecute(null));
    }

    [Fact]
    public async Task Empty_scene_has_an_active_preview_and_sources_do_not_change_its_placeholder()
    {
        var workspace = new StudioWorkspaceViewModel();
        var sourceRuntime = new FakeSourceRuntime();
        var viewModel = CreateViewModel(
            new FakeSceneRuntime(), workspace, sourceRuntime: sourceRuntime,
            previewRuntime: new FakeScenePreviewRuntime());

        Assert.Equal("Aucune scène sélectionnée.", viewModel.PreviewPlaceholderText);

        var scene = CreateScene(viewModel, "Preview");
        Assert.Same(scene, workspace.ActiveScene);
        Assert.Equal("", viewModel.PreviewPlaceholderText);

        await viewModel.ApplyAddSourceResultAsync(new AddSourceResult.Video(
            new CaptureSourceOption("window-1", "Fenêtre", VideoCaptureKind.Window)));

        Assert.Equal("", viewModel.PreviewPlaceholderText);
        Assert.Same(scene, workspace.ActiveScene);
    }

    [Fact]
    public void Creating_or_selecting_a_scene_updates_the_global_selection()
    {
        var workspace = new StudioWorkspaceViewModel();
        var viewModel = CreateViewModel(new FakeSceneRuntime(), workspace);
        var onAir = CreateScene(viewModel, "Première");

        // Creating a scene selects it globally so both pages show the same scene.
        var second = CreateScene(viewModel, "Deuxième");
        Assert.Same(second, viewModel.SelectedScene);
        Assert.Same(second, workspace.ActiveScene);
        Assert.False(onAir.IsSelected);
        Assert.True(second.IsSelected);
        Assert.False(onAir.IsActive);
        Assert.True(second.IsActive);

        // Selecting from Scenes updates the same workspace selection used by Studio.
        workspace.SetRecordingState(true);
        viewModel.SelectSceneCommand.Execute(onAir);
        Assert.Same(onAir, viewModel.SelectedScene);
        Assert.Same(onAir, workspace.ActiveScene);
        Assert.True(onAir.IsSelected);
        Assert.False(second.IsSelected);
        Assert.True(onAir.IsActive);
        Assert.False(second.IsActive);

        // Selecting from Studio updates the Scenes page projection as well.
        workspace.SelectScene(second);
        Assert.Same(second, viewModel.SelectedScene);
        Assert.Same(second, workspace.ActiveScene);
        Assert.True(second.IsSelected);
        Assert.True(second.IsActive);
        Assert.False(onAir.IsSelected);
        Assert.False(onAir.IsActive);
    }

    [Fact]
    public void Preview_canvas_updates_when_video_settings_are_saved()
    {
        var directory = Directory.CreateTempSubdirectory("castor-preview-settings-");
        var settingsPath = Path.Combine(directory.FullName, "settings.json");
        try
        {
            var settings = new SettingsService(settingsPath);
            var viewModel = CreateViewModel(new FakeSceneRuntime(), settingsService: settings);

            Assert.Equal((1920, 1080), (viewModel.BaseCanvasWidth, viewModel.BaseCanvasHeight));

            settings.Save(new CastorApplication.Models.Settings.ApplicationSettings
            {
                SelectedBaseResolutionIndex = 3
            });

            Assert.Equal((2560, 1440), (viewModel.BaseCanvasWidth, viewModel.BaseCanvasHeight));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
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
        Assert.Equal("nom refusé", viewModel.RenameSceneError);

        runtime.Rename = (_, _) => SceneRuntimeResult.Success("Demandée 2");
        viewModel.ConfirmRenameSceneCommand.Execute(null);

        Assert.Equal("Demandée 2", scene.Name);
        Assert.Null(viewModel.SceneBeingRenamed);
        Assert.Equal("", viewModel.RenameSceneError);
        Assert.False(viewModel.ConfirmRenameSceneCommand.CanExecute(null));
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
        // The selected scene is also the active workspace scene.
        Assert.False(first.IsSelected);
        Assert.True(second.IsSelected);
        Assert.False(first.IsActive);
        Assert.True(second.IsActive);

        runtime.Remove = _ => SceneRuntimeResult.Failure("suppression refusée");
        viewModel.DeleteSceneCommand.Execute(second);

        Assert.Equal(2, viewModel.Scenes.Count);
        Assert.Same(second, viewModel.SelectedScene);
        Assert.Same(second, workspace.ActiveScene);
        Assert.Equal("suppression refusée", viewModel.DeleteSceneError);

        runtime.Remove = _ => SceneRuntimeResult.Success();
        viewModel.DeleteSceneCommand.Execute(second);

        Assert.Single(viewModel.Scenes);
        Assert.Same(first, viewModel.SelectedScene);
        Assert.True(first.IsSelected);
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

    [Fact]
    public async Task Adding_multiple_video_sources_uses_native_names_and_keeps_every_item()
    {
        var sourceRuntime = new FakeSourceRuntime();
        var suffix = 0;
        sourceRuntime.Add = (_, request) =>
            SourceRuntimeResult.Success(suffix++ == 0 ? request.RequestedName : $"{request.RequestedName} 2");
        var viewModel = CreateViewModel(new FakeSceneRuntime(), sourceRuntime: sourceRuntime);
        CreateScene(viewModel, "Scène");
        var option = new CaptureSourceOption("window-1", "Navigateur", VideoCaptureKind.Window);

        await viewModel.ApplyAddSourceResultAsync(new AddSourceResult.Video(option));
        await viewModel.ApplyAddSourceResultAsync(new AddSourceResult.Video(option));

        Assert.Equal(2, viewModel.SelectedScene!.Sources.Count);
        // La liste suit l'empilement du moteur, premier plan en tête : la source la plus
        // récemment ajoutée passe devant les précédentes.
        Assert.Equal(["Navigateur 2", "Navigateur"], viewModel.SelectedScene.Sources.Select(source => source.Name));
        Assert.Equal(2, sourceRuntime.AddedRequests.Count);
    }

    [Fact]
    public async Task Three_sources_can_be_reordered_entirely_from_the_interface()
    {
        var sourceRuntime = new FakeSourceRuntime();
        var viewModel = CreateViewModel(new FakeSceneRuntime(), sourceRuntime: sourceRuntime);
        CreateScene(viewModel, "Composition");
        await AddVideoSourcesAsync(viewModel, "Caméra", "Écran", "Overlay");

        var scene = viewModel.SelectedScene!;
        Assert.Equal(["Overlay", "Écran", "Caméra"], Names(scene));

        // Glisser-déposer : lâcher une source sur une autre lui prend son rang.
        viewModel.MoveSourceHere(scene.Sources[2], scene.Sources[0]);
        Assert.Equal(["Caméra", "Overlay", "Écran"], Names(scene));
        Assert.Equal(0, sourceRuntime.RequestedLayerIndexes[^1]);

        // Équivalent clavier : un plan en arrière, puis un plan en avant.
        viewModel.LowerSourceCommand.Execute(scene.Sources[0]);
        Assert.Equal(["Overlay", "Caméra", "Écran"], Names(scene));
        Assert.Equal(1, sourceRuntime.RequestedLayerIndexes[^1]);

        viewModel.RaiseSourceCommand.Execute(scene.Sources[2]);
        Assert.Equal(["Overlay", "Écran", "Caméra"], Names(scene));
        Assert.Equal(1, sourceRuntime.RequestedLayerIndexes[^1]);
        Assert.Equal("", viewModel.SourceOperationStatus);
    }

    [Fact]
    public async Task Reloading_a_scene_takes_the_order_back_from_the_engine()
    {
        var sourceRuntime = new FakeSourceRuntime();
        var viewModel = CreateViewModel(new FakeSceneRuntime(), sourceRuntime: sourceRuntime);
        var scene = CreateScene(viewModel, "Composition");
        var other = CreateScene(viewModel, "Autre");
        viewModel.SelectSceneCommand.Execute(scene);
        await AddVideoSourcesAsync(viewModel, "Caméra", "Écran", "Overlay");

        viewModel.MoveSourceHere(scene.Sources[2], scene.Sources[0]);
        var engineOrder = Names(scene);

        // Une liste locale qui dériverait n'est pas une source de vérité : la scène rouverte
        // se recale sur ce que le moteur détient.
        scene.Sources.Move(0, 2);
        Assert.NotEqual(engineOrder, Names(scene));

        viewModel.SelectSceneCommand.Execute(other);
        viewModel.SelectSceneCommand.Execute(scene);

        Assert.Equal(engineOrder, Names(scene));
    }

    [Fact]
    public async Task A_refused_reorder_leaves_the_list_on_what_the_engine_holds()
    {
        var sourceRuntime = new FakeSourceRuntime();
        var viewModel = CreateViewModel(new FakeSceneRuntime(), sourceRuntime: sourceRuntime);
        CreateScene(viewModel, "Composition");
        await AddVideoSourcesAsync(viewModel, "Caméra", "Écran", "Overlay");

        var scene = viewModel.SelectedScene!;
        var before = Names(scene);
        sourceRuntime.Move = (_, _, _) => SourceRuntimeResult.Failure("réordonnancement refusé");

        viewModel.MoveSourceHere(scene.Sources[2], scene.Sources[0]);

        Assert.Equal(before, Names(scene));
        Assert.Equal("réordonnancement refusé", viewModel.SourceOperationStatus);
    }

    [Fact]
    public async Task The_two_ends_of_the_stack_cannot_be_pushed_further()
    {
        var sourceRuntime = new FakeSourceRuntime();
        var viewModel = CreateViewModel(new FakeSceneRuntime(), sourceRuntime: sourceRuntime);
        CreateScene(viewModel, "Composition");
        await AddVideoSourcesAsync(viewModel, "Caméra", "Overlay");

        var scene = viewModel.SelectedScene!;
        Assert.False(viewModel.RaiseSourceCommand.CanExecute(scene.Sources[0]));
        Assert.True(viewModel.LowerSourceCommand.CanExecute(scene.Sources[0]));
        Assert.True(viewModel.RaiseSourceCommand.CanExecute(scene.Sources[^1]));
        Assert.False(viewModel.LowerSourceCommand.CanExecute(scene.Sources[^1]));

        viewModel.RaiseSourceCommand.Execute(scene.Sources[0]);
        Assert.Empty(sourceRuntime.RequestedLayerIndexes);
    }

    [Fact]
    public async Task An_order_the_engine_cannot_give_back_is_reported_rather_than_swallowed()
    {
        var sourceRuntime = new FakeSourceRuntime();
        var viewModel = CreateViewModel(new FakeSceneRuntime(), sourceRuntime: sourceRuntime);
        CreateScene(viewModel, "Composition");
        await AddVideoSourcesAsync(viewModel, "Caméra", "Overlay");

        var scene = viewModel.SelectedScene!;
        var before = Names(scene);
        sourceRuntime.OrderIsUnreadable = true;

        // Le moteur accepte le déplacement mais ne sait plus rendre l'ordre : la liste reste
        // en arrière, ce qui doit se voir plutôt que passer pour un geste sans effet.
        viewModel.LowerSourceCommand.Execute(scene.Sources[0]);

        Assert.Equal(before, Names(scene));
        Assert.Equal("ordre illisible", viewModel.SourceOperationStatus);
    }

    private static async Task AddVideoSourcesAsync(ScenesViewModel viewModel, params string[] labels)
    {
        foreach (var label in labels)
        {
            await viewModel.ApplyAddSourceResultAsync(new AddSourceResult.Video(
                new CaptureSourceOption(label, label, VideoCaptureKind.Window)));
        }
    }

    private static string[] Names(SceneItemViewModel scene) =>
        scene.Sources.Select(source => source.Name).ToArray();

    [Fact]
    public async Task Native_add_failure_does_not_change_local_sources()
    {
        var sourceRuntime = new FakeSourceRuntime
        {
            Add = (_, _) => SourceRuntimeResult.Failure("périphérique refusé")
        };
        var viewModel = CreateViewModel(new FakeSceneRuntime(), sourceRuntime: sourceRuntime);
        CreateScene(viewModel, "Scène");

        await viewModel.ApplyAddSourceResultAsync(new AddSourceResult.Audio(
            new AudioSourceOption("micro-1", "Micro", AudioCaptureKind.Microphone)));

        Assert.Empty(viewModel.SelectedScene!.Sources);
        Assert.Equal("périphérique refusé", viewModel.SourceOperationStatus);
    }

    [Fact]
    public async Task Media_picker_adds_one_native_media_source_and_cancellation_adds_nothing()
    {
        var sourceRuntime = new FakeSourceRuntime();
        var canceled = CreateViewModel(new FakeSceneRuntime(), sourceRuntime: sourceRuntime);
        CreateScene(canceled, "Annulée");
        await canceled.ApplyAddSourceResultAsync(new AddSourceResult.Media());
        Assert.Empty(canceled.SelectedScene!.Sources);

        var viewModel = CreateViewModel(
            new FakeSceneRuntime(), sourceRuntime: sourceRuntime, mediaPath: @"C:\media\clip.mp4");
        CreateScene(viewModel, "Média");
        await viewModel.ApplyAddSourceResultAsync(new AddSourceResult.Media());

        var source = Assert.Single(viewModel.SelectedScene!.Sources);
        Assert.Equal(SourceKind.Media, source.Kind);
        Assert.Equal(@"C:\media\clip.mp4", source.OriginPath);
        Assert.IsType<SourceAddRequest.Media>(Assert.Single(sourceRuntime.AddedRequests));
    }

    [Fact]
    public async Task Remove_and_loop_changes_update_local_state_only_after_native_success()
    {
        var sourceRuntime = new FakeSourceRuntime();
        var viewModel = CreateViewModel(
            new FakeSceneRuntime(), sourceRuntime: sourceRuntime, mediaPath: @"C:\media\clip.mp4");
        CreateScene(viewModel, "Média");
        await viewModel.ApplyAddSourceResultAsync(new AddSourceResult.Media());
        var source = Assert.Single(viewModel.SelectedScene!.Sources);

        sourceRuntime.SetLoop = (_, _, _) => SourceRuntimeResult.Failure("boucle refusée");
        viewModel.ToggleMediaLoopCommand.Execute(source);
        Assert.True(source.Loop);

        sourceRuntime.SetLoop = (_, _, _) => SourceRuntimeResult.Success();
        viewModel.ToggleMediaLoopCommand.Execute(source);
        Assert.False(source.Loop);

        sourceRuntime.Remove = (_, _) => SourceRuntimeResult.Failure("suppression refusée");
        viewModel.RemoveSourceCommand.Execute(source);
        Assert.Single(viewModel.SelectedScene.Sources);

        sourceRuntime.Remove = (_, _) => SourceRuntimeResult.Success();
        viewModel.RemoveSourceCommand.Execute(source);
        Assert.Empty(viewModel.SelectedScene.Sources);
    }

    [Fact]
    public async Task Import_merges_legacy_video_audio_pair_into_one_native_media_source()
    {
        const string path = @"C:\media\legacy.mp4";
        var imported = new SceneDefinition
        {
            Name = "Importée",
            Sources =
            [
                new SourceDefinition { Name = "Vidéo", Kind = SourceKind.Video, Origin = SourceOrigin.File, OriginPath = path },
                new SourceDefinition { Name = "Audio", Kind = SourceKind.Audio, Origin = SourceOrigin.File, OriginPath = path }
            ]
        };
        var sourceRuntime = new FakeSourceRuntime();
        var viewModel = CreateViewModel(
            new FakeSceneRuntime(), imported: [imported], sourceRuntime: sourceRuntime);

        await viewModel.ImportScenesCommand.ExecuteAsync(null);

        var source = Assert.Single(Assert.Single(viewModel.Scenes).Sources);
        Assert.Equal(SourceKind.Media, source.Kind);
        Assert.Single(sourceRuntime.AddedRequests);
    }

    [Fact]
    public void Sorting_the_displayed_sources_never_reorders_the_scene()
    {
        var workspace = new StudioWorkspaceViewModel();
        var scene = workspace.CreateScene("Scène");
        workspace.AddSource(scene, new SourceDefinition { Name = "Zoom", Kind = SourceKind.Video });
        workspace.AddSource(scene, new SourceDefinition { Name = "Audio", Kind = SourceKind.Audio });
        workspace.AddSource(scene, new SourceDefinition { Name = "Media", Kind = SourceKind.Media });
        var viewModel = CreateViewModel(new FakeSceneRuntime(), workspace);

        Assert.False(viewModel.IsSourceListCustomized);
        viewModel.SourceSort = SourceListSort.NameAscending;

        Assert.Equal(["Audio", "Media", "Zoom"], viewModel.DisplayedSources.Select(source => source.Name));
        Assert.Equal(["Zoom", "Audio", "Media"], scene.Sources.Select(source => source.Name));
        Assert.True(viewModel.IsSourceListCustomized);
    }

    [Fact]
    public void Displayed_sources_follow_the_filter_and_scene_changes()
    {
        var workspace = new StudioWorkspaceViewModel();
        var scene = workspace.CreateScene("Scène");
        workspace.AddSource(scene, new SourceDefinition { Name = "Caméra", Kind = SourceKind.Video });
        var viewModel = CreateViewModel(new FakeSceneRuntime(), workspace);
        Assert.True(viewModel.SourceSortOptions[0].IsSelected);

        var audioOption = viewModel.SourceFilterOptions.Single(option => option.Filter == SourceListFilter.Audio);
        viewModel.ApplySourceListOptionCommand.Execute(audioOption);

        Assert.Empty(viewModel.DisplayedSources);
        Assert.Equal("0 sur 1", viewModel.SourceListSummary);
        Assert.Equal("Aucune source de ce type.", viewModel.SourceListPlaceholder);
        Assert.True(audioOption.IsSelected);
        Assert.False(viewModel.SourceFilterOptions[0].IsSelected);

        workspace.AddSource(scene, new SourceDefinition { Name = "Micro", Kind = SourceKind.Audio });

        Assert.Equal(["Micro"], viewModel.DisplayedSources.Select(source => source.Name));
        Assert.Equal("1 sur 2", viewModel.SourceListSummary);
        Assert.Equal("", viewModel.SourceListPlaceholder);

        var other = workspace.CreateScene("Autre");
        workspace.SelectScene(other);
        workspace.AddSource(scene, new SourceDefinition { Name = "Micro 2", Kind = SourceKind.Audio });

        Assert.Empty(viewModel.DisplayedSources);
        Assert.Equal("Aucune source. Ajoutez-en une avec +.", viewModel.SourceListPlaceholder);
    }

    [Fact]
    public void Source_rename_applies_the_native_name_and_keeps_editing_on_failure()
    {
        var workspace = new StudioWorkspaceViewModel();
        var scene = workspace.CreateScene("Scène");
        var source = workspace.AddSource(scene, new SourceDefinition { Name = "Caméra", Kind = SourceKind.Video });
        var sourceRuntime = new FakeSourceRuntime { Rename = (_, _, _) => SourceRuntimeResult.Failure("nom refusé") };
        var viewModel = CreateViewModel(new FakeSceneRuntime(), workspace, sourceRuntime: sourceRuntime);

        viewModel.BeginRenameSourceCommand.Execute(source);
        Assert.True(source.IsRenaming);
        Assert.Equal("Caméra", viewModel.RenameSourceName);

        viewModel.RenameSourceName = "Plateau";
        viewModel.ConfirmRenameSourceCommand.Execute(null);

        Assert.Equal("Caméra", source.Name);
        Assert.True(source.IsRenaming);
        Assert.Equal("nom refusé", viewModel.SourceOperationStatus);

        sourceRuntime.Rename = (_, _, name) => SourceRuntimeResult.Success($"{name} 2");
        viewModel.ConfirmRenameSourceCommand.Execute(null);

        Assert.Equal("Plateau 2", source.Name);
        Assert.False(source.IsRenaming);
        Assert.Null(viewModel.SourceBeingRenamed);
        Assert.Equal("", viewModel.SourceOperationStatus);
    }

    [Fact]
    public void Source_rename_with_empty_or_same_name_or_cancel_does_not_reach_the_engine()
    {
        var workspace = new StudioWorkspaceViewModel();
        var scene = workspace.CreateScene("Scène");
        var source = workspace.AddSource(scene, new SourceDefinition { Name = "Caméra", Kind = SourceKind.Video });
        var sourceRuntime = new FakeSourceRuntime();
        var viewModel = CreateViewModel(new FakeSceneRuntime(), workspace, sourceRuntime: sourceRuntime);

        viewModel.BeginRenameSourceCommand.Execute(source);
        viewModel.RenameSourceName = "   ";
        viewModel.ConfirmRenameSourceCommand.Execute(null);
        Assert.False(source.IsRenaming);

        viewModel.BeginRenameSourceCommand.Execute(source);
        viewModel.ConfirmRenameSourceCommand.Execute(null);
        Assert.False(source.IsRenaming);

        viewModel.BeginRenameSourceCommand.Execute(source);
        viewModel.RenameSourceName = "Autre";
        viewModel.CancelRenameSourceCommand.Execute(null);

        Assert.False(source.IsRenaming);
        Assert.Equal("Caméra", source.Name);
        Assert.Equal(0, sourceRuntime.RenameCalls);
    }

    [Fact]
    public void Renaming_a_source_resorts_a_name_sorted_list_without_reordering_the_scene()
    {
        var workspace = new StudioWorkspaceViewModel();
        var scene = workspace.CreateScene("Scène");
        var first = workspace.AddSource(scene, new SourceDefinition { Name = "Alpha", Kind = SourceKind.Video });
        workspace.AddSource(scene, new SourceDefinition { Name = "Beta", Kind = SourceKind.Audio });
        var viewModel = CreateViewModel(new FakeSceneRuntime(), workspace);
        viewModel.SourceSort = SourceListSort.NameAscending;

        viewModel.BeginRenameSourceCommand.Execute(first);
        viewModel.RenameSourceName = "Zulu";
        viewModel.ConfirmRenameSourceCommand.Execute(null);

        Assert.Equal(["Beta", "Zulu"], viewModel.DisplayedSources.Select(source => source.Name));
        Assert.Equal(["Zulu", "Beta"], scene.Sources.Select(source => source.Name));
    }

    [Fact]
    public void Switching_scene_ends_a_source_rename()
    {
        var workspace = new StudioWorkspaceViewModel();
        var scene = workspace.CreateScene("Scène");
        var source = workspace.AddSource(scene, new SourceDefinition { Name = "Caméra", Kind = SourceKind.Video });
        var viewModel = CreateViewModel(new FakeSceneRuntime(), workspace);

        viewModel.BeginRenameSourceCommand.Execute(source);
        workspace.SelectScene(workspace.CreateScene("Autre"));

        Assert.False(source.IsRenaming);
        Assert.Null(viewModel.SourceBeingRenamed);
    }

    [Fact]
    public async Task Import_writes_only_the_scenes_left_checked_from_every_picked_file()
    {
        var first = new SceneDefinition { Name = "Plateau" };
        var second = new SceneDefinition { Name = "Interview" };
        var third = new SceneDefinition { Name = "Générique" };
        var collection = new FakeSceneCollection(new Dictionary<string, IReadOnlyList<SceneDefinition>>
        {
            [@"C:\a.json"] = [first, second],
            [@"C:\b.json"] = [third],
        });
        var dialog = new FakeTransferDialogService(choose: shown =>
            shown.Items.Single(item => item.Name == "Interview").IsChecked = false);
        var viewModel = CreateViewModel(
            new FakeSceneRuntime(),
            filePicker: new FakeFilePicker([@"C:\a.json", @"C:\b.json", @"C:\broken.json"], null),
            sceneCollection: collection,
            transferDialog: dialog);

        await viewModel.ImportScenesCommand.ExecuteAsync(null);

        // Everything found was offered, checked, with the file it came from and the unreadable one reported.
        Assert.Equal(3, dialog.Shown!.Items.Count);
        Assert.Contains("b.json", dialog.Shown.Items.Single(item => item.Name == "Générique").Detail);
        Assert.Contains("broken.json", Assert.Single(dialog.Shown.FileErrors));
        Assert.Equal(["Plateau", "Générique"], viewModel.Scenes.Select(scene => scene.Name));
    }

    [Fact]
    public async Task Cancelling_the_import_selection_writes_nothing()
    {
        var runtime = new FakeSceneRuntime();
        var created = 0;
        runtime.Create = (_, name) => { created++; return SceneRuntimeResult.Success(name); };
        var viewModel = CreateViewModel(
            runtime,
            imported: [new SceneDefinition { Name = "Plateau" }],
            transferDialog: new FakeTransferDialogService(confirm: false));

        await viewModel.ImportScenesCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.Scenes);
        Assert.Equal(0, created);
    }

    [Fact]
    public async Task Importing_a_scene_already_in_the_project_adds_a_copy()
    {
        var workspace = new StudioWorkspaceViewModel();
        var viewModel = CreateViewModel(new FakeSceneRuntime(), workspace);
        var existing = CreateScene(viewModel, "Plateau");
        var sameScene = new SceneDefinition { Id = existing.Id, Name = "Plateau" };
        var dialog = new FakeTransferDialogService();
        viewModel = CreateViewModel(new FakeSceneRuntime(), workspace, imported: [sameScene], transferDialog: dialog);

        await viewModel.ImportScenesCommand.ExecuteAsync(null);

        Assert.True(dialog.Shown!.Items.Single().HasCollision);
        Assert.Equal(2, workspace.Scenes.Count);
        Assert.NotEqual(workspace.Scenes[0].Id, workspace.Scenes[1].Id);
    }

    [Fact]
    public async Task Export_pre_checks_the_selection_and_writes_what_is_checked()
    {
        var collection = new FakeSceneCollection(new Dictionary<string, IReadOnlyList<SceneDefinition>>());
        var dialog = new FakeTransferDialogService();
        var viewModel = CreateViewModel(
            new FakeSceneRuntime(),
            filePicker: new FakeFilePicker([], null, exportPath: @"C:\export.json"),
            sceneCollection: collection,
            transferDialog: dialog);
        CreateScene(viewModel, "Plateau");
        var interview = CreateScene(viewModel, "Interview");
        interview.IsMultiSelected = true;

        await viewModel.ExportScenesCommand.ExecuteAsync(null);

        Assert.Equal([false, true], dialog.Shown!.Items.Select(item => item.IsChecked));
        Assert.Equal(["Interview"], collection.Saved.Select(scene => scene.Name));
    }

    [Fact]
    public async Task Cancelling_the_export_selection_writes_nothing()
    {
        var collection = new FakeSceneCollection(new Dictionary<string, IReadOnlyList<SceneDefinition>>());
        var viewModel = CreateViewModel(
            new FakeSceneRuntime(),
            filePicker: new FakeFilePicker([], null, exportPath: @"C:\export.json"),
            sceneCollection: collection,
            transferDialog: new FakeTransferDialogService(confirm: false));
        CreateScene(viewModel, "Plateau");

        await viewModel.ExportScenesCommand.ExecuteAsync(null);

        Assert.Equal(0, collection.SaveCalls);
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
        IReadOnlyList<SceneDefinition>? imported = null,
        FakeSourceRuntime? sourceRuntime = null,
        string? mediaPath = null,
        IScenePreviewRuntime? previewRuntime = null,
        SettingsService? settingsService = null,
        FakeTransferDialogService? transferDialog = null,
        FakeFilePicker? filePicker = null,
        FakeSceneCollection? sceneCollection = null) =>
        new(
            workspace ?? new StudioWorkspaceViewModel(),
            new UnavailableStudioRuntime(),
            previewRuntime ?? new UnavailableScenePreviewRuntime(),
            runtime,
            sourceRuntime ??= new FakeSourceRuntime(),
            filePicker ?? new FakeFilePicker(imported == null ? [] : ["scenes.json"], mediaPath),
            sceneCollection ?? new FakeSceneCollection(new Dictionary<string, IReadOnlyList<SceneDefinition>> { ["scenes.json"] = imported ?? [] }),
            new FakeDialogFactory(sourceRuntime),
            new FakeDialogService(),
            transferDialog ?? new FakeTransferDialogService(),
            settingsService,
            new VideoCanvasResolutionResolver(
                settingsService,
                new TestResolutionProvider(new VideoCanvasResolution(1920, 1080))));

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

    private sealed class FakeScenePreviewRuntime : IScenePreviewRuntime
    {
        public bool IsAvailable => true;
        public string UnavailableMessage => "";
        public event EventHandler? PreviewResetRequested
        {
            add { }
            remove { }
        }

        public Task<StudioRuntimeResult> StartPreviewAsync(
            SceneDefinition scene,
            IntPtr windowHandle,
            uint width,
            uint height,
            CancellationToken cancellationToken) =>
            Task.FromResult(StudioRuntimeResult.Success());

        public void ResizePreview(IntPtr windowHandle, uint width, uint height)
        {
        }

        public void SetCompositionOverlay(IntPtr windowHandle, CompositionOverlay overlay)
        {
        }

        public Task<StudioRuntimeResult> StopPreviewAsync(IntPtr windowHandle, Guid sceneId, CancellationToken cancellationToken) =>
            Task.FromResult(StudioRuntimeResult.Success());
    }

    private sealed class FakeSourceRuntime : ISourceRuntime
    {
        public Func<Guid, SourceAddRequest, SourceRuntimeResult> Add { get; set; } =
            (_, request) => SourceRuntimeResult.Success(request.RequestedName);
        public Func<Guid, Guid, SourceRuntimeResult> Remove { get; set; } =
            (_, _) => SourceRuntimeResult.Success();
        public Func<Guid, Guid, bool, SourceRuntimeResult> SetLoop { get; set; } =
            (_, _, _) => SourceRuntimeResult.Success();
        public Func<Guid, Guid, int, SourceRuntimeResult> Move { get; set; } =
            (_, _, _) => SourceRuntimeResult.Success();
        public List<SourceAddRequest> AddedRequests { get; } = [];
        public int RemoveCalls { get; private set; }
        public List<bool> LoopValues { get; } = [];
        public List<int> RequestedLayerIndexes { get; } = [];
        public bool OrderIsUnreadable { get; set; }

        // Empilement tenu par le faux moteur, du premier plan vers l'arrière-plan, pour que
        // les tests exercent la relecture plutôt qu'un ordre supposé côté ViewModel.
        private readonly Dictionary<Guid, List<Guid>> _layers = [];

        public bool IsAvailable => true;
        public string UnavailableMessage => "";

        public Task<SourceCatalog> EnumerateSourcesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SourceCatalog([], []));

        public SourceRuntimeResult AddSource(Guid sceneId, SourceAddRequest request)
        {
            AddedRequests.Add(request);
            var result = Add(sceneId, request);
            // Comme libobs, une source ajoutée se place au premier plan.
            if (result.IsSuccess) Layers(sceneId).Insert(0, request.SourceId);
            return result;
        }

        public SourceRuntimeResult RemoveSource(Guid sceneId, Guid sourceId)
        {
            RemoveCalls++;
            var result = Remove(sceneId, sourceId);
            if (result.IsSuccess) Layers(sceneId).Remove(sourceId);
            return result;
        }

        public SourceRuntimeResult SetMediaLoop(Guid sceneId, Guid sourceId, bool loop)
        {
            LoopValues.Add(loop);
            return SetLoop(sceneId, sourceId, loop);
        }

        public Func<Guid, Guid, string, SourceRuntimeResult> Rename { get; set; } =
            (_, _, name) => SourceRuntimeResult.Success(name.Trim());
        public int RenameCalls { get; private set; }

        public SourceRuntimeResult RenameSource(Guid sceneId, Guid sourceId, string requestedName)
        {
            RenameCalls++;
            return Rename(sceneId, sourceId, requestedName);
        }

        public SourceOrderResult GetSourceOrder(Guid sceneId) =>
            OrderIsUnreadable
                ? SourceOrderResult.Failure("ordre illisible")
                : SourceOrderResult.Success(Layers(sceneId).ToArray());

        // La composition elle-même est éprouvée par SceneCompositionViewModelTests ; ici, il
        // suffit que la scène en rende une pour que le canvas suive les gestes.
        public SceneCompositionResult GetSceneComposition(Guid sceneId) =>
            SceneCompositionResult.Success(new SceneComposition(1920, 1080, []));

        public SourceRuntimeResult MoveSource(Guid sceneId, Guid sourceId, int layerIndex)
        {
            RequestedLayerIndexes.Add(layerIndex);
            var result = Move(sceneId, sourceId, layerIndex);
            if (!result.IsSuccess) return result;

            var layers = Layers(sceneId);
            var current = layers.IndexOf(sourceId);
            if (current < 0 || layerIndex < 0 || layerIndex >= layers.Count)
                return SourceRuntimeResult.Failure("rang hors de la pile");

            layers.RemoveAt(current);
            layers.Insert(layerIndex, sourceId);
            return result;
        }

        private List<Guid> Layers(Guid sceneId)
        {
            if (_layers.TryGetValue(sceneId, out var layers)) return layers;

            layers = [];
            _layers[sceneId] = layers;
            return layers;
        }
    }

    private sealed class FakeFilePicker(IReadOnlyList<string> importPaths, string? mediaPath, string? exportPath = null) : IFilePickerService
    {
        public Task<string?> PickRecordingOutputFolderAsync(string? initialPath = null) => Task.FromResult<string?>(null);
        public Task<string?> PickVideoFileAsync() => Task.FromResult<string?>(null);
        public Task<string?> PickAudioFileAsync() => Task.FromResult<string?>(null);
        public Task<string?> PickMediaFileAsync() => Task.FromResult(mediaPath);
        public Task<string?> PickSceneExportFileAsync() => Task.FromResult(exportPath);
        public Task<IReadOnlyList<string>> PickSceneImportFilesAsync() => Task.FromResult(importPaths);
    }

    private sealed class FakeSceneCollection(IReadOnlyDictionary<string, IReadOnlyList<SceneDefinition>> files) : ISceneCollectionService
    {
        public List<SceneDefinition> Saved { get; } = [];
        public int SaveCalls { get; private set; }

        public Task SaveAsync(string path, IReadOnlyCollection<SceneDefinition> definitions, CancellationToken cancellationToken)
        {
            SaveCalls++;
            Saved.AddRange(definitions);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SceneDefinition>> LoadAsync(string path, CancellationToken cancellationToken) =>
            files.TryGetValue(path, out var scenes)
                ? Task.FromResult(scenes)
                : throw new InvalidDataException("fichier illisible");
    }

    // Stands in for the operator: optionally changes what is checked, then confirms or cancels.
    private sealed class FakeTransferDialogService(bool confirm = true, Action<SceneTransferDialogViewModel>? choose = null) : ISceneTransferDialogService
    {
        public SceneTransferDialogViewModel? Shown { get; private set; }

        public Task<bool> ShowAsync(SceneTransferDialogViewModel viewModel)
        {
            Shown = viewModel;
            choose?.Invoke(viewModel);
            return Task.FromResult(confirm);
        }
    }

    private sealed class FakeDialogFactory(ISourceRuntime runtime) : IAddSourceDialogViewModelFactory
    {
        public AddSourceDialogViewModel Create(SceneItemViewModel? scene) => new(runtime, scene);
    }

    private sealed class FakeDialogService : IAddSourceDialogService
    {
        public Task<AddSourceResult?> ShowAsync(AddSourceDialogViewModel viewModel) => Task.FromResult<AddSourceResult?>(null);
    }
}
