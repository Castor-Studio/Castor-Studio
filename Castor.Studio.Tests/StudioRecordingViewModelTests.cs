using CastorApplication.Models.Settings;
using CastorApplication.Models.Settings.Providers;
using CastorApplication.Models.Studio;
using CastorApplication.Services.Auth.Storage;
using CastorApplication.Services.Dialogs;
using CastorApplication.Services.Settings;
using CastorApplication.Services.Studio;
using CastorApplication.ViewModels.Multicam;
using CastorApplication.ViewModels.Scenes;
using CastorApplication.ViewModels.Studio;

namespace Castor.Studio.Tests;

public sealed class StudioRecordingViewModelTests
{
    [Fact]
    public async Task Start_recording_supports_a_2k_base_and_output()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var settingsService = new SettingsService(Path.Combine(directory, "settings.json"));
            settingsService.Save(new ApplicationSettings
            {
                OutputPath = directory,
                SelectedBaseResolutionIndex = 3,
                SelectedOutputResolutionIndex = 3
            });
            var workspace = new StudioWorkspaceViewModel();
            var scene = workspace.CreateScene("Enregistrement 2K");
            workspace.AddSource(scene, new SourceDefinition { Name = "Écran", Kind = SourceKind.Video });
            var recordingRuntime = new FakeRecordingRuntime();
            var viewModel = new StudioViewModel(
                workspace,
                new FakeStudioRuntime(),
                new UnavailableScenePreviewRuntime(),
                recordingRuntime,
                new FakeStreamingRuntime(),
                new FakeProviderStore(),
                settingsService
            );

            await viewModel.StartRecordingCommand.ExecuteAsync(null);

            var request = Assert.IsType<RecordingRequest>(recordingRuntime.Request);
            Assert.Equal(2560, request.BaseWidth);
            Assert.Equal(1440, request.BaseHeight);
            Assert.Equal(2560, request.OutputWidth);
            Assert.Equal(1440, request.OutputHeight);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Start_recording_uses_the_active_native_scene_and_all_saved_settings()
    {
        var directory = CreateTemporaryDirectory();
        var settingsPath = Path.Combine(directory, "settings.json");
        try
        {
            var settingsService = new SettingsService(settingsPath);
            settingsService.Save(new ApplicationSettings
            {
                OutputPath = directory,
                SelectedOutputFormatIndex = 2,
                SelectedBaseResolutionIndex = 2,
                SelectedOutputResolutionIndex = 1,
                SelectedFpsIndex = 2,
                VideoBitrate = 7_500,
                SelectedSampleRateIndex = 1,
                SelectedChannelsIndex = 1,
                SelectedAudioBitrateIndex = 0
            });
            var workspace = new StudioWorkspaceViewModel();
            var scene = workspace.CreateScene("Enregistrement");
            workspace.AddSource(scene, new SourceDefinition { Name = "Écran", Kind = SourceKind.Video });
            var recordingRuntime = new FakeRecordingRuntime();
            var viewModel = new StudioViewModel(
                workspace,
                new FakeStudioRuntime(),
                new UnavailableScenePreviewRuntime(),
                recordingRuntime,
                new FakeStreamingRuntime(),
                new FakeProviderStore(),
                settingsService
            );

            await viewModel.StartRecordingCommand.ExecuteAsync(null);

            var request = Assert.IsType<RecordingRequest>(recordingRuntime.Request);
            Assert.Equal(scene.Id, request.SceneId);
            Assert.Equal(RecordingContainer.WebM, request.Container);
            Assert.Equal(1280, request.BaseWidth);
            Assert.Equal(720, request.BaseHeight);
            Assert.Equal(1280, request.OutputWidth);
            Assert.Equal(720, request.OutputHeight);
            Assert.Equal(25, request.Fps);
            Assert.Equal(7_500, request.VideoBitrateKbps);
            Assert.Equal(320, request.AudioBitrateKbps);
            Assert.Equal(44_100, request.AudioSampleRate);
            Assert.Equal(1, request.AudioChannels);
            Assert.Equal(directory, Path.GetDirectoryName(request.OutputPath));
            Assert.EndsWith(".webm", request.OutputPath);
            Assert.True(workspace.IsRecording);

            var otherScene = workspace.CreateScene("Autre scène");
            workspace.SelectScene(otherScene);

            Assert.Equal(otherScene, workspace.ActiveScene);
            Assert.Equal(scene.Id, recordingRuntime.Request?.SceneId);
            Assert.True(workspace.IsRecording);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Runtime_failure_does_not_change_recording_state()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var settingsService = new SettingsService(Path.Combine(directory, "settings.json"));
            settingsService.Save(new ApplicationSettings { OutputPath = directory });
            var workspace = new StudioWorkspaceViewModel();
            var scene = workspace.CreateScene("Échec");
            workspace.AddSource(scene, new SourceDefinition { Name = "Caméra", Kind = SourceKind.Video });
            var recordingRuntime = new FakeRecordingRuntime
            {
                StartResult = StudioRuntimeResult.Failure("échec output")
            };
            var viewModel = new StudioViewModel(
                workspace,
                new FakeStudioRuntime(),
                new UnavailableScenePreviewRuntime(),
                recordingRuntime,
                new FakeStreamingRuntime(),
                new FakeProviderStore(),
                settingsService
            );

            await viewModel.StartRecordingCommand.ExecuteAsync(null);

            Assert.False(workspace.IsRecording);
            Assert.Equal("échec output", viewModel.RecordError.Text);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Native_stop_event_updates_the_ui_and_reports_its_error()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var settingsService = new SettingsService(Path.Combine(directory, "settings.json"));
            settingsService.Save(new ApplicationSettings { OutputPath = directory });
            var workspace = new StudioWorkspaceViewModel();
            var scene = workspace.CreateScene("Arrêt");
            workspace.AddSource(scene, new SourceDefinition { Name = "Écran", Kind = SourceKind.Video });
            var recordingRuntime = new FakeRecordingRuntime();
            var viewModel = new StudioViewModel(
                workspace,
                new FakeStudioRuntime(),
                new UnavailableScenePreviewRuntime(),
                recordingRuntime,
                new FakeStreamingRuntime(),
                new FakeProviderStore(),
                settingsService
            );
            await viewModel.StartRecordingCommand.ExecuteAsync(null);

            recordingRuntime.RaiseState(RecordingState.Idle, "Disque plein");

            Assert.False(workspace.IsRecording);
            Assert.Equal("Disque plein", viewModel.RecordError.Text);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Runtime_states_drive_the_recording_state_and_invalid_ones_are_ignored()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var settingsService = new SettingsService(Path.Combine(directory, "settings.json"));
            settingsService.Save(new ApplicationSettings { OutputPath = directory });
            var workspace = new StudioWorkspaceViewModel();
            var scene = workspace.CreateScene("États");
            workspace.AddSource(scene, new SourceDefinition { Name = "Écran", Kind = SourceKind.Video });
            var recordingRuntime = new FakeRecordingRuntime();
            var viewModel = new StudioViewModel(
                workspace,
                new FakeStudioRuntime(),
                new UnavailableScenePreviewRuntime(),
                recordingRuntime,
                new FakeStreamingRuntime(),
                new FakeProviderStore(),
                settingsService
            );
            await viewModel.StartRecordingCommand.ExecuteAsync(null);
            Assert.Equal(RecordingState.Recording, workspace.RecordingState);

            recordingRuntime.RaiseState(RecordingState.Paused);
            Assert.Equal(RecordingState.Paused, viewModel.RecordingState);
            Assert.True(viewModel.IsOnAir);
            Assert.Equal("En pause", viewModel.SceneBarStatusText);

            recordingRuntime.RaiseState(RecordingState.Recording);
            recordingRuntime.RaiseState(RecordingState.Stopping);
            Assert.Equal(RecordingState.Stopping, workspace.RecordingState);
            Assert.True(workspace.IsRecording);

            // Stopping only ends in Idle: a late "started" must not revive the session.
            recordingRuntime.RaiseState(RecordingState.Recording);
            Assert.Equal(RecordingState.Stopping, workspace.RecordingState);

            recordingRuntime.RaiseState(RecordingState.Idle);
            Assert.Equal(RecordingState.Idle, workspace.RecordingState);
            Assert.False(viewModel.IsOnAir);
            Assert.Equal("Prêt", viewModel.SceneBarStatusText);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Stop_command_returns_to_idle()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var settingsService = new SettingsService(Path.Combine(directory, "settings.json"));
            settingsService.Save(new ApplicationSettings { OutputPath = directory });
            var workspace = new StudioWorkspaceViewModel();
            var scene = workspace.CreateScene("Stop");
            workspace.AddSource(scene, new SourceDefinition { Name = "Écran", Kind = SourceKind.Video });
            var viewModel = new StudioViewModel(
                workspace,
                new FakeStudioRuntime(),
                new UnavailableScenePreviewRuntime(),
                new FakeRecordingRuntime(),
                new FakeStreamingRuntime(),
                new FakeProviderStore(),
                settingsService
            );
            await viewModel.StartRecordingCommand.ExecuteAsync(null);

            await viewModel.StopRecordingCommand.ExecuteAsync(null);

            Assert.Equal(RecordingState.Idle, workspace.RecordingState);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Pause_is_only_available_while_recording()
    {
        using var fixture = CreateRecordingFixture();
        Assert.False(fixture.ViewModel.TogglePauseRecordingCommand.CanExecute(null));

        await fixture.ViewModel.StartRecordingCommand.ExecuteAsync(null);

        Assert.True(fixture.ViewModel.TogglePauseRecordingCommand.CanExecute(null));
        Assert.Equal("PAUSE", fixture.ViewModel.PauseButtonText);
        Assert.Contains(StudioViewModel.PauseShortcutText, fixture.ViewModel.PauseToolTip);
    }

    [Fact]
    public async Task Pausing_and_resuming_toggles_the_state_and_the_paused_indicators()
    {
        using var fixture = CreateRecordingFixture();
        await fixture.ViewModel.StartRecordingCommand.ExecuteAsync(null);

        fixture.ViewModel.TogglePauseRecordingCommand.Execute(null);

        Assert.Equal(1, fixture.Runtime.PauseCalls);
        Assert.Equal(RecordingState.Paused, fixture.Workspace.RecordingState);
        Assert.True(fixture.ViewModel.IsRecordingPaused);
        Assert.Equal("REPRENDRE", fixture.ViewModel.PauseButtonText);
        Assert.Equal("En pause", fixture.ViewModel.SceneBarStatusText);
        Assert.Equal("PAUSE", MulticamViewModel.OutputStateText(fixture.Workspace));
        Assert.True(fixture.ViewModel.IsOnAir);

        fixture.ViewModel.TogglePauseRecordingCommand.Execute(null);

        Assert.Equal(1, fixture.Runtime.ResumeCalls);
        Assert.Equal(RecordingState.Recording, fixture.Workspace.RecordingState);
        Assert.Equal("PAUSE", fixture.ViewModel.PauseButtonText);
        Assert.Equal("Enregistrement", fixture.ViewModel.SceneBarStatusText);
        Assert.Equal("REC", MulticamViewModel.OutputStateText(fixture.Workspace));
    }

    [Fact]
    public async Task Pause_can_be_toggled_several_times_in_the_same_recording()
    {
        using var fixture = CreateRecordingFixture();
        await fixture.ViewModel.StartRecordingCommand.ExecuteAsync(null);

        for (var i = 0; i < 3; i++)
        {
            fixture.ViewModel.TogglePauseRecordingCommand.Execute(null);
            fixture.ViewModel.TogglePauseRecordingCommand.Execute(null);
        }

        Assert.Equal(3, fixture.Runtime.PauseCalls);
        Assert.Equal(3, fixture.Runtime.ResumeCalls);
        Assert.Equal(RecordingState.Recording, fixture.Workspace.RecordingState);
        // One recording, one file: pausing never starts a new output.
        Assert.NotNull(fixture.Runtime.Request);
    }

    [Fact]
    public async Task Timer_freezes_while_paused_and_excludes_pauses()
    {
        using var fixture = CreateRecordingFixture();
        await fixture.ViewModel.StartRecordingCommand.ExecuteAsync(null);

        fixture.Time.Now += TimeSpan.FromSeconds(10);
        fixture.ViewModel.TogglePauseRecordingCommand.Execute(null);
        Assert.Equal("00:00:10", fixture.ViewModel.StreamTimerText);

        fixture.Time.Now += TimeSpan.FromMinutes(5);
        fixture.ViewModel.RefreshSessionTimer();
        Assert.Equal("00:00:10", fixture.ViewModel.StreamTimerText);

        fixture.ViewModel.TogglePauseRecordingCommand.Execute(null);
        fixture.Time.Now += TimeSpan.FromSeconds(5);
        fixture.ViewModel.RefreshSessionTimer();
        Assert.Equal("00:00:15", fixture.ViewModel.StreamTimerText);
    }

    [Fact]
    public async Task Runtime_pause_events_drive_the_paused_state()
    {
        using var fixture = CreateRecordingFixture();
        await fixture.ViewModel.StartRecordingCommand.ExecuteAsync(null);

        fixture.Runtime.RaiseState(RecordingState.Paused);
        Assert.True(fixture.ViewModel.IsRecordingPaused);

        fixture.Runtime.RaiseState(RecordingState.Recording);
        Assert.False(fixture.ViewModel.IsRecordingPaused);
    }

    [Fact]
    public async Task Pause_is_disabled_with_the_reason_when_the_output_cannot_pause()
    {
        using var fixture = CreateRecordingFixture(new FakeRecordingRuntime
        {
            PauseUnavailableReason = "Le format WebM ne permet pas la pause."
        });

        await fixture.ViewModel.StartRecordingCommand.ExecuteAsync(null);

        Assert.False(fixture.ViewModel.TogglePauseRecordingCommand.CanExecute(null));
        Assert.Equal("Le format WebM ne permet pas la pause.", fixture.ViewModel.PauseToolTip);
    }

    [Fact]
    public async Task Pause_failure_reports_the_error_and_keeps_recording()
    {
        using var fixture = CreateRecordingFixture(new FakeRecordingRuntime
        {
            PauseResult = StudioRuntimeResult.Failure("La mise en pause a échoué.")
        });
        await fixture.ViewModel.StartRecordingCommand.ExecuteAsync(null);

        fixture.ViewModel.TogglePauseRecordingCommand.Execute(null);

        Assert.Equal(RecordingState.Recording, fixture.Workspace.RecordingState);
        Assert.Equal("La mise en pause a échoué.", fixture.ViewModel.RecordError.Text);
    }

    [Fact]
    public async Task Resume_failure_reports_the_error_and_stays_paused()
    {
        using var fixture = CreateRecordingFixture(new FakeRecordingRuntime
        {
            ResumeResult = StudioRuntimeResult.Failure("La reprise a échoué.")
        });
        await fixture.ViewModel.StartRecordingCommand.ExecuteAsync(null);
        fixture.ViewModel.TogglePauseRecordingCommand.Execute(null);

        fixture.ViewModel.TogglePauseRecordingCommand.Execute(null);

        Assert.Equal(RecordingState.Paused, fixture.Workspace.RecordingState);
        Assert.Equal("La reprise a échoué.", fixture.ViewModel.RecordError.Text);
    }

    [Fact]
    public async Task Stopping_while_paused_finalizes_the_recording()
    {
        using var fixture = CreateRecordingFixture();
        await fixture.ViewModel.StartRecordingCommand.ExecuteAsync(null);
        fixture.ViewModel.TogglePauseRecordingCommand.Execute(null);

        await fixture.ViewModel.StopRecordingCommand.ExecuteAsync(null);

        Assert.Equal(RecordingState.Idle, fixture.Workspace.RecordingState);
        Assert.False(fixture.ViewModel.IsRecordingPaused);
        Assert.Equal("00:00:00", fixture.ViewModel.StreamTimerText);
        Assert.False(fixture.ViewModel.TogglePauseRecordingCommand.CanExecute(null));
    }

    [Fact]
    public async Task Cancel_is_available_while_recording_and_while_paused()
    {
        using var fixture = CreateRecordingFixture();
        Assert.False(fixture.ViewModel.CancelRecordingCommand.CanExecute(null));

        await fixture.ViewModel.StartRecordingCommand.ExecuteAsync(null);
        Assert.True(fixture.ViewModel.CancelRecordingCommand.CanExecute(null));

        fixture.ViewModel.TogglePauseRecordingCommand.Execute(null);
        Assert.True(fixture.ViewModel.CancelRecordingCommand.CanExecute(null));

        fixture.Runtime.RaiseState(RecordingState.Stopping);
        Assert.False(fixture.ViewModel.CancelRecordingCommand.CanExecute(null));
    }

    [Fact]
    public async Task Confirmed_cancel_stops_deletes_the_file_and_returns_to_idle()
    {
        using var fixture = CreateRecordingFixture();
        await fixture.ViewModel.StartRecordingCommand.ExecuteAsync(null);
        var path = fixture.CreateRecordedFile();

        await fixture.ViewModel.CancelRecordingCommand.ExecuteAsync(null);

        Assert.Equal([path], fixture.CancelDialog.AskedPaths);
        Assert.Equal(1, fixture.Runtime.StopCalls);
        Assert.Equal(RecordingState.Idle, fixture.Workspace.RecordingState);
        Assert.False(File.Exists(path));
        Assert.Equal("", fixture.ViewModel.RecordError.Text);
    }

    [Fact]
    public async Task Cancel_from_pause_deletes_the_file_too()
    {
        using var fixture = CreateRecordingFixture();
        await fixture.ViewModel.StartRecordingCommand.ExecuteAsync(null);
        var path = fixture.CreateRecordedFile();
        fixture.ViewModel.TogglePauseRecordingCommand.Execute(null);

        await fixture.ViewModel.CancelRecordingCommand.ExecuteAsync(null);

        Assert.Equal(RecordingState.Idle, fixture.Workspace.RecordingState);
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dismissing_the_confirmation_leaves_the_recording_untouched(bool paused)
    {
        using var fixture = CreateRecordingFixture();
        fixture.CancelDialog.Answer = false;
        await fixture.ViewModel.StartRecordingCommand.ExecuteAsync(null);
        var path = fixture.CreateRecordedFile();
        if (paused) fixture.ViewModel.TogglePauseRecordingCommand.Execute(null);

        await fixture.ViewModel.CancelRecordingCommand.ExecuteAsync(null);

        Assert.Equal(0, fixture.Runtime.StopCalls);
        Assert.Equal(paused ? RecordingState.Paused : RecordingState.Recording, fixture.Workspace.RecordingState);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task A_file_that_cannot_be_deleted_is_reported_with_its_path_and_the_ui_still_goes_idle()
    {
        using var fixture = CreateRecordingFixture();
        await fixture.ViewModel.StartRecordingCommand.ExecuteAsync(null);
        var path = fixture.CreateRecordedFile();

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            await fixture.ViewModel.CancelRecordingCommand.ExecuteAsync(null);

        Assert.Equal(RecordingState.Idle, fixture.Workspace.RecordingState);
        Assert.True(File.Exists(path));
        Assert.Contains(path, fixture.ViewModel.RecordError.Text);
    }

    [Fact]
    public async Task A_stop_failure_keeps_the_recording_and_its_file()
    {
        using var fixture = CreateRecordingFixture(new FakeRecordingRuntime
        {
            StopResult = StudioRuntimeResult.Failure("Arrêt de l'enregistrement impossible.")
        });
        await fixture.ViewModel.StartRecordingCommand.ExecuteAsync(null);
        var path = fixture.CreateRecordedFile();

        await fixture.ViewModel.CancelRecordingCommand.ExecuteAsync(null);

        Assert.Equal(RecordingState.Recording, fixture.Workspace.RecordingState);
        Assert.True(File.Exists(path));
        Assert.Equal("Arrêt de l'enregistrement impossible.", fixture.ViewModel.RecordError.Text);
    }

    [Fact]
    public async Task Cancel_deletes_nothing_but_the_recording_file()
    {
        using var fixture = CreateRecordingFixture();
        var neighbour = Path.Combine(fixture.Directory, "Castor_previous_take.mp4");
        File.WriteAllText(neighbour, "keep me");
        await fixture.ViewModel.StartRecordingCommand.ExecuteAsync(null);
        fixture.CreateRecordedFile();

        await fixture.ViewModel.CancelRecordingCommand.ExecuteAsync(null);

        Assert.True(File.Exists(neighbour));
        Assert.True(File.Exists(Path.Combine(fixture.Directory, "settings.json")));
    }

    [Fact]
    public async Task A_normal_stop_keeps_the_file()
    {
        using var fixture = CreateRecordingFixture();
        await fixture.ViewModel.StartRecordingCommand.ExecuteAsync(null);
        var path = fixture.CreateRecordedFile();

        await fixture.ViewModel.StopRecordingCommand.ExecuteAsync(null);

        Assert.Empty(fixture.CancelDialog.AskedPaths);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task By_default_only_the_program_is_recorded()
    {
        using var fixture = CreateRecordingFixture();
        AddVideoScene(fixture, "Caméra");

        await fixture.ViewModel.StartRecordingCommand.ExecuteAsync(null);

        var request = Assert.Single(fixture.Runtime.Requests);
        Assert.True(request.FollowsProgram);
        Assert.StartsWith("Castor_", Path.GetFileName(request.OutputPath));
        Assert.DoesNotContain("Caméra", request.OutputPath);
        var session = Assert.Single(fixture.ViewModel.RecordingSessions);
        Assert.True(session.FollowsProgram);
        Assert.Equal(request.SessionId, session.Id);
    }

    [Fact]
    public async Task Each_selected_scene_gets_its_own_session_and_file()
    {
        using var fixture = CreateRecordingFixture();
        var camera = AddVideoScene(fixture, "Caméra");
        var wide = AddVideoScene(fixture, "Plan large");
        Select(fixture, camera);
        Select(fixture, wide);

        await fixture.ViewModel.StartRecordingCommand.ExecuteAsync(null);

        Assert.Equal(3, fixture.Runtime.Requests.Count);
        Assert.Equal([true, false, false], fixture.Runtime.Requests.Select(r => r.FollowsProgram));
        Assert.Equal(
            [fixture.Workspace.ActiveScene!.Id, camera.Id, wide.Id],
            fixture.Runtime.Requests.Select(r => r.SceneId));
        Assert.Equal(3, fixture.Runtime.Requests.Select(r => r.SessionId).Distinct().Count());
        Assert.Equal(3, fixture.Runtime.Requests.Select(r => r.OutputPath).Distinct().Count());
        Assert.Contains("Caméra", fixture.Runtime.Requests[1].OutputPath);
        Assert.Contains("Plan large", fixture.Runtime.Requests[2].OutputPath);
        Assert.Equal(["Scène active", "Caméra", "Plan large"], fixture.ViewModel.RecordingSessions.Select(s => s.Name));
        Assert.Equal(RecordingState.Recording, fixture.Workspace.RecordingState);
    }

    [Fact]
    public async Task Scenes_can_be_recorded_without_the_program()
    {
        using var fixture = CreateRecordingFixture();
        var camera = AddVideoScene(fixture, "Caméra");
        fixture.ViewModel.RecordProgram = false;
        Assert.False(fixture.ViewModel.StartRecordingCommand.CanExecute(null));

        Select(fixture, camera);
        Assert.True(fixture.ViewModel.StartRecordingCommand.CanExecute(null));
        await fixture.ViewModel.StartRecordingCommand.ExecuteAsync(null);

        var request = Assert.Single(fixture.Runtime.Requests);
        Assert.False(request.FollowsProgram);
        Assert.Equal(camera.Id, request.SceneId);
    }

    [Fact]
    public async Task A_target_that_fails_is_named_while_the_others_keep_recording()
    {
        using var fixture = CreateRecordingFixture(new FakeRecordingRuntime
        {
            StartResultFor = request => request.FollowsProgram
                ? StudioRuntimeResult.Success()
                : StudioRuntimeResult.Failure("encodeur indisponible")
        });
        var camera = AddVideoScene(fixture, "Caméra");
        Select(fixture, camera);

        await fixture.ViewModel.StartRecordingCommand.ExecuteAsync(null);

        var session = Assert.Single(fixture.ViewModel.RecordingSessions);
        Assert.True(session.FollowsProgram);
        Assert.Equal("Caméra : encodeur indisponible", fixture.ViewModel.RecordError.Text);
        Assert.Equal(RecordingState.Recording, fixture.Workspace.RecordingState);
    }

    [Fact]
    public async Task Stopping_one_session_leaves_the_others_recording()
    {
        using var fixture = CreateRecordingFixture();
        Select(fixture, AddVideoScene(fixture, "Caméra"));
        await fixture.ViewModel.StartRecordingCommand.ExecuteAsync(null);
        var camera = fixture.ViewModel.RecordingSessions[1];

        await fixture.ViewModel.StopSessionCommand.ExecuteAsync(camera);

        Assert.Equal([camera.Id], fixture.Runtime.StoppedSessions);
        var program = Assert.Single(fixture.ViewModel.RecordingSessions);
        Assert.True(program.FollowsProgram);
        Assert.Equal(RecordingState.Recording, fixture.Workspace.RecordingState);
        Assert.Equal("Enregistrement", fixture.ViewModel.SceneBarStatusText);
    }

    [Fact]
    public async Task Cancelling_one_session_deletes_only_its_file()
    {
        using var fixture = CreateRecordingFixture();
        Select(fixture, AddVideoScene(fixture, "Caméra"));
        await fixture.ViewModel.StartRecordingCommand.ExecuteAsync(null);
        var programPath = CreateFile(fixture.Runtime.Requests[0].OutputPath);
        var cameraPath = CreateFile(fixture.Runtime.Requests[1].OutputPath);
        var camera = fixture.ViewModel.RecordingSessions[1];

        await fixture.ViewModel.CancelSessionCommand.ExecuteAsync(camera);

        Assert.Equal([cameraPath], fixture.CancelDialog.AskedPaths);
        Assert.False(File.Exists(cameraPath));
        Assert.True(File.Exists(programPath));
        Assert.Single(fixture.ViewModel.RecordingSessions);
    }

    [Fact]
    public async Task Cancel_all_confirms_every_file_and_deletes_them()
    {
        using var fixture = CreateRecordingFixture();
        Select(fixture, AddVideoScene(fixture, "Caméra"));
        await fixture.ViewModel.StartRecordingCommand.ExecuteAsync(null);
        var paths = fixture.Runtime.Requests.Select(r => CreateFile(r.OutputPath)).ToList();

        await fixture.ViewModel.CancelRecordingCommand.ExecuteAsync(null);

        Assert.Equal(paths, Assert.Single(fixture.CancelDialog.Requests));
        Assert.All(paths, path => Assert.False(File.Exists(path)));
        Assert.Empty(fixture.ViewModel.RecordingSessions);
        Assert.Equal(RecordingState.Idle, fixture.Workspace.RecordingState);
    }

    [Fact]
    public async Task Stop_all_stops_every_session()
    {
        using var fixture = CreateRecordingFixture();
        Select(fixture, AddVideoScene(fixture, "Caméra"));
        await fixture.ViewModel.StartRecordingCommand.ExecuteAsync(null);

        await fixture.ViewModel.StopRecordingCommand.ExecuteAsync(null);

        Assert.Equal(fixture.Runtime.Requests.Select(r => r.SessionId), fixture.Runtime.StoppedSessions);
        Assert.Empty(fixture.ViewModel.RecordingSessions);
        Assert.Equal(RecordingState.Idle, fixture.Workspace.RecordingState);
    }

    [Fact]
    public async Task The_overall_state_is_rec_while_one_session_records_and_paused_once_all_are()
    {
        using var fixture = CreateRecordingFixture();
        Select(fixture, AddVideoScene(fixture, "Caméra"));
        await fixture.ViewModel.StartRecordingCommand.ExecuteAsync(null);
        var program = fixture.ViewModel.RecordingSessions[0];
        var camera = fixture.ViewModel.RecordingSessions[1];

        fixture.ViewModel.ToggleSessionPauseCommand.Execute(camera);

        Assert.Equal([camera.Id], fixture.Runtime.PausedSessions);
        Assert.True(camera.IsPaused);
        Assert.Equal(RecordingState.Recording, fixture.Workspace.RecordingState);
        Assert.Equal("REC", MulticamViewModel.OutputStateText(fixture.Workspace));

        fixture.ViewModel.TogglePauseRecordingCommand.Execute(null);

        Assert.True(program.IsPaused);
        Assert.Equal(RecordingState.Paused, fixture.Workspace.RecordingState);
        Assert.Equal("PAUSE", MulticamViewModel.OutputStateText(fixture.Workspace));
        Assert.Equal("En pause", fixture.ViewModel.SceneBarStatusText);

        fixture.ViewModel.TogglePauseRecordingCommand.Execute(null);

        Assert.False(program.IsPaused);
        Assert.False(camera.IsPaused);
        Assert.Equal(RecordingState.Recording, fixture.Workspace.RecordingState);
    }

    [Fact]
    public async Task Each_session_keeps_its_own_timer()
    {
        using var fixture = CreateRecordingFixture();
        Select(fixture, AddVideoScene(fixture, "Caméra"));
        await fixture.ViewModel.StartRecordingCommand.ExecuteAsync(null);
        var program = fixture.ViewModel.RecordingSessions[0];
        var camera = fixture.ViewModel.RecordingSessions[1];

        fixture.Time.Now += TimeSpan.FromSeconds(10);
        fixture.ViewModel.ToggleSessionPauseCommand.Execute(camera);
        fixture.Time.Now += TimeSpan.FromSeconds(20);
        fixture.ViewModel.RefreshSessionTimer();

        Assert.Equal("00:00:30", program.TimerText);
        Assert.Equal("00:00:10", camera.TimerText);
    }

    [Fact]
    public async Task A_runtime_error_names_its_session_once_several_run()
    {
        using var fixture = CreateRecordingFixture();
        Select(fixture, AddVideoScene(fixture, "Caméra"));
        await fixture.ViewModel.StartRecordingCommand.ExecuteAsync(null);
        var camera = fixture.ViewModel.RecordingSessions[1];

        fixture.Runtime.RaiseState(RecordingState.Idle, "Disque plein", camera.Id);

        Assert.Equal("Caméra : Disque plein", fixture.ViewModel.RecordError.Text);
        Assert.Single(fixture.ViewModel.RecordingSessions);
        Assert.Equal(RecordingState.Recording, fixture.Workspace.RecordingState);
    }

    [Fact]
    public async Task Recorded_scenes_are_marked_and_the_program_mark_follows_the_active_scene()
    {
        using var fixture = CreateRecordingFixture();
        var program = fixture.Workspace.ActiveScene!;
        var camera = AddVideoScene(fixture, "Caméra");
        var other = AddVideoScene(fixture, "Autre");
        Select(fixture, camera);
        await fixture.ViewModel.StartRecordingCommand.ExecuteAsync(null);

        Assert.True(program.IsBeingRecorded);
        Assert.True(camera.IsBeingRecorded);
        Assert.False(other.IsBeingRecorded);

        fixture.Workspace.SelectScene(other);

        Assert.False(program.IsBeingRecorded);
        Assert.True(other.IsBeingRecorded);
        Assert.True(camera.IsBeingRecorded);

        await fixture.ViewModel.StopRecordingCommand.ExecuteAsync(null);

        Assert.All(fixture.Workspace.Scenes, scene => Assert.False(scene.IsBeingRecorded));
    }

    [Fact]
    public void Selecting_many_scenes_warns_about_the_load()
    {
        using var fixture = CreateRecordingFixture();
        var camera = AddVideoScene(fixture, "Caméra");
        Select(fixture, camera);
        Assert.Equal("", fixture.ViewModel.RecordingLoadWarning);

        Select(fixture, AddVideoScene(fixture, "Plan large"));

        Assert.Equal(StudioViewModel.HeavyRecordingTargetCount, fixture.ViewModel.RecordingTargetCount);
        Assert.Contains("processeur", fixture.ViewModel.RecordingLoadWarning);
    }

    [Fact]
    public async Task Switching_scene_while_recording_re_points_the_output()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var settingsService = new SettingsService(Path.Combine(directory, "settings.json"));
            settingsService.Save(new ApplicationSettings { OutputPath = directory });
            var workspace = new StudioWorkspaceViewModel();
            var first = workspace.CreateScene("Plateau");
            workspace.AddSource(first, new SourceDefinition { Name = "Écran", Kind = SourceKind.Video });
            var second = workspace.CreateScene("Caméra");
            workspace.AddSource(second, new SourceDefinition { Name = "Webcam", Kind = SourceKind.Video });
            workspace.SelectScene(first);
            var recordingRuntime = new FakeRecordingRuntime();
            var viewModel = new StudioViewModel(
                workspace, new FakeStudioRuntime(), new UnavailableScenePreviewRuntime(), recordingRuntime,
                new FakeStreamingRuntime(), new FakeProviderStore(), settingsService);

            // Idle: the output is not running, so a scene switch has nothing to re-point -
            // starting a recording reads the active scene itself.
            viewModel.ActiveScene = second;
            Assert.Empty(recordingRuntime.SwitchedScenes);

            workspace.SelectScene(first);
            await viewModel.StartRecordingCommand.ExecuteAsync(null);
            Assert.Equal(first.Id, Assert.IsType<RecordingRequest>(recordingRuntime.Request).SceneId);

            // Recording: the switch has to reach the output, not just the preview.
            viewModel.ActiveScene = second;

            Assert.Equal([second.Id], recordingRuntime.SwitchedScenes);
            Assert.Equal("", viewModel.RecordError.Text);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task A_refused_scene_switch_surfaces_the_native_message()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var settingsService = new SettingsService(Path.Combine(directory, "settings.json"));
            settingsService.Save(new ApplicationSettings { OutputPath = directory });
            var workspace = new StudioWorkspaceViewModel();
            var first = workspace.CreateScene("Plateau");
            workspace.AddSource(first, new SourceDefinition { Name = "Écran", Kind = SourceKind.Video });
            var second = workspace.CreateScene("Caméra");
            var recordingRuntime = new FakeRecordingRuntime();
            var viewModel = new StudioViewModel(
                workspace, new FakeStudioRuntime(), new UnavailableScenePreviewRuntime(), recordingRuntime,
                new FakeStreamingRuntime(), new FakeProviderStore(), settingsService);

            await viewModel.StartRecordingCommand.ExecuteAsync(null);
            recordingRuntime.SwitchResult = StudioRuntimeResult.Failure("scène refusée");
            viewModel.ActiveScene = second;

            Assert.Equal("scène refusée", viewModel.RecordError.Text);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Saving_a_new_output_folder_updates_the_recalled_folder()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var settingsService = new SettingsService(Path.Combine(directory, "settings.json"));
            settingsService.Save(new ApplicationSettings { OutputPath = directory });
            var viewModel = new StudioViewModel(
                new StudioWorkspaceViewModel(), new FakeStudioRuntime(), new UnavailableScenePreviewRuntime(),
                new FakeRecordingRuntime(), new FakeStreamingRuntime(), new FakeProviderStore(), settingsService);
            var newFolder = Path.Combine(directory, "Captures");

            settingsService.Save(new ApplicationSettings { OutputPath = newFolder });

            Assert.Equal(newFolder, viewModel.RecordingOutputDirectory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Transition_choice_is_loaded_from_and_saved_to_the_settings()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var settingsService = new SettingsService(Path.Combine(directory, "settings.json"));
            settingsService.Save(new ApplicationSettings
            {
                SceneTransitionKind = SceneTransitionKind.Slide,
                SceneTransitionDurationMs = 1000
            });
            var viewModel = new StudioViewModel(
                new StudioWorkspaceViewModel(), new FakeStudioRuntime(), new UnavailableScenePreviewRuntime(),
                new FakeRecordingRuntime(), new FakeStreamingRuntime(), new FakeProviderStore(), settingsService);

            // Loading the kind first must not write it back with the default duration.
            Assert.Equal(SceneTransitionKind.Slide, viewModel.SelectedTransition?.Kind);
            Assert.Equal(1000, viewModel.TransitionDurationMs);
            Assert.Equal(1000, settingsService.Load().SceneTransitionDurationMs);

            viewModel.SelectedTransition = SceneTransitionOption.For(SceneTransitionKind.Cut);
            viewModel.TransitionDurationMs = 500;

            var saved = settingsService.Load();
            Assert.Equal(SceneTransitionKind.Cut, saved.SceneTransitionKind);
            Assert.Equal(500, saved.SceneTransitionDurationMs);
            Assert.False(viewModel.IsTransitionAnimated);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Duration_slider_moves_from_preset_to_preset_and_greys_out_on_a_cut()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var settingsService = new SettingsService(Path.Combine(directory, "settings.json"));
            settingsService.Save(new ApplicationSettings());
            var viewModel = new StudioViewModel(
                new StudioWorkspaceViewModel(), new FakeStudioRuntime(), new UnavailableScenePreviewRuntime(),
                new FakeRecordingRuntime(), new FakeStreamingRuntime(), new FakeProviderStore(), settingsService);

            Assert.Equal(1, viewModel.TransitionDurationStep);
            Assert.Equal("300 ms", viewModel.TransitionDurationText);

            // A drag lands between two notches: the duration snaps to the nearest preset.
            viewModel.TransitionDurationStep = 3.4;
            Assert.Equal(750, viewModel.TransitionDurationMs);
            Assert.Equal(750, settingsService.Load().SceneTransitionDurationMs);

            viewModel.SelectedTransition = SceneTransitionOption.For(SceneTransitionKind.Cut);
            Assert.False(viewModel.IsTransitionAnimated);
            Assert.Equal("", viewModel.TransitionDurationText);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void A_scene_tile_knows_when_its_scene_has_no_picture()
    {
        var workspace = new StudioWorkspaceViewModel();
        var scene = workspace.CreateScene("Pause");
        Assert.False(scene.HasVideoSource);

        workspace.AddSource(scene, new SourceDefinition { Name = "Micro", Kind = SourceKind.Audio });
        Assert.False(scene.HasVideoSource);

        workspace.AddSource(scene, new SourceDefinition { Name = "Caméra", Kind = SourceKind.Video });
        Assert.True(scene.HasVideoSource);
    }

    [Theory]
    [InlineData(-10, SceneTransition.MinDurationMs)]
    [InlineData(60_000, SceneTransition.MaxDurationMs)]
    public void Out_of_range_transition_duration_is_clamped(int stored, int expected)
    {
        var settings = new ApplicationSettings { SceneTransitionDurationMs = stored };

        Assert.Equal(expected, settings.ToSceneTransition().DurationMs);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"castor-viewmodel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeRecordingRuntime : IRecordingRuntime
    {
        public bool IsAvailable => true;
        public string UnavailableMessage => "";
        public List<RecordingRequest> Requests { get; } = [];
        public RecordingRequest? Request => Requests.FirstOrDefault();
        public StudioRuntimeResult StartResult { get; init; } = StudioRuntimeResult.Success();
        // Per-request result, for one target failing while the others start.
        public Func<RecordingRequest, StudioRuntimeResult>? StartResultFor { get; init; }

        public event EventHandler<RecordingStateChangedEventArgs>? StateChanged;

        public Task<StudioRuntimeResult> StartRecordingAsync(RecordingRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(StartResultFor?.Invoke(request) ?? StartResult);
        }

        public StudioRuntimeResult StopResult { get; set; } = StudioRuntimeResult.Success();
        public int StopCalls { get; private set; }
        public List<Guid> StoppedSessions { get; } = [];

        public Task<StudioRuntimeResult> StopRecordingAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            StopCalls++;
            StoppedSessions.Add(sessionId);
            return Task.FromResult(StopResult);
        }

        public Task<StudioRuntimeResult> StopRecordingAsync(CancellationToken cancellationToken)
        {
            StopCalls++;
            return Task.FromResult(StopResult);
        }

        public List<Guid> SwitchedScenes { get; } = [];
        public StudioRuntimeResult SwitchResult { get; set; } = StudioRuntimeResult.Success();

        public StudioRuntimeResult SwitchRecordingScene(Guid sceneId)
        {
            SwitchedScenes.Add(sceneId);
            return SwitchResult;
        }

        // Without a session, the event is for the last recording started.
        public void RaiseState(RecordingState state, string message = "", Guid? sessionId = null) =>
            StateChanged?.Invoke(this, new RecordingStateChangedEventArgs(
                sessionId ?? Requests[^1].SessionId, state, message));

        public string PauseUnavailableReason { get; set; } = "";
        public StudioRuntimeResult PauseResult { get; set; } = StudioRuntimeResult.Success();
        public StudioRuntimeResult ResumeResult { get; set; } = StudioRuntimeResult.Success();
        public int PauseCalls { get; private set; }
        public int ResumeCalls { get; private set; }
        public List<Guid> PausedSessions { get; } = [];

        public string GetPauseUnavailableReason(Guid sessionId) => PauseUnavailableReason;

        public StudioRuntimeResult PauseRecording(Guid sessionId)
        {
            PauseCalls++;
            PausedSessions.Add(sessionId);
            return PauseResult;
        }

        public StudioRuntimeResult ResumeRecording(Guid sessionId)
        {
            ResumeCalls++;
            return ResumeResult;
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeCancelRecordingDialog : ICancelRecordingDialogService
    {
        public bool Answer { get; set; } = true;
        public List<string> AskedPaths { get; } = [];

        public List<IReadOnlyList<string>> Requests { get; } = [];

        public Task<bool> ConfirmAsync(IReadOnlyList<string> outputPaths)
        {
            Requests.Add(outputPaths);
            AskedPaths.AddRange(outputPaths);
            return Task.FromResult(Answer);
        }
    }

    private sealed class RecordingFixture(
        string directory,
        StudioWorkspaceViewModel workspace,
        FakeRecordingRuntime runtime,
        StudioViewModel viewModel,
        ManualTimeProvider time,
        FakeCancelRecordingDialog cancelDialog) : IDisposable
    {
        public string Directory { get; } = directory;
        public StudioWorkspaceViewModel Workspace { get; } = workspace;
        public FakeRecordingRuntime Runtime { get; } = runtime;
        public StudioViewModel ViewModel { get; } = viewModel;
        public ManualTimeProvider Time { get; } = time;
        public FakeCancelRecordingDialog CancelDialog { get; } = cancelDialog;

        // The fake runtime writes nothing: stands in for the file the output would create.
        public string CreateRecordedFile()
        {
            var path = Assert.IsType<RecordingRequest>(Runtime.Request).OutputPath;
            File.WriteAllText(path, "frames");
            return path;
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }

    private static SceneItemViewModel AddVideoScene(RecordingFixture fixture, string name)
    {
        var scene = fixture.Workspace.CreateScene(name);
        fixture.Workspace.AddSource(scene, new SourceDefinition { Name = "Caméra", Kind = SourceKind.Video });
        return scene;
    }

    private static void Select(RecordingFixture fixture, SceneItemViewModel scene) =>
        fixture.ViewModel.RecordingSceneOptions.Single(option => option.Scene == scene).IsSelected = true;

    // The fake runtime writes nothing: stands in for the file an output would create.
    private static string CreateFile(string path)
    {
        File.WriteAllText(path, "frames");
        return path;
    }

    private static RecordingFixture CreateRecordingFixture(FakeRecordingRuntime? runtime = null)
    {
        var cancelDialog = new FakeCancelRecordingDialog();
        var directory = CreateTemporaryDirectory();
        var settingsService = new SettingsService(Path.Combine(directory, "settings.json"));
        settingsService.Save(new ApplicationSettings { OutputPath = directory });
        var workspace = new StudioWorkspaceViewModel();
        var scene = workspace.CreateScene("Pause");
        workspace.AddSource(scene, new SourceDefinition { Name = "Écran", Kind = SourceKind.Video });
        runtime ??= new FakeRecordingRuntime();
        var time = new ManualTimeProvider();
        var viewModel = new StudioViewModel(
            workspace,
            new FakeStudioRuntime(),
            new UnavailableScenePreviewRuntime(),
            runtime,
            new FakeStreamingRuntime(),
            new FakeProviderStore(),
            settingsService,
            timeProvider: time,
            cancelRecordingDialog: cancelDialog
        );
        return new RecordingFixture(directory, workspace, runtime, viewModel, time, cancelDialog);
    }

    private sealed class FakeStudioRuntime : IStudioRuntime
    {
        public bool IsAvailable => false;
        public string UnavailableMessage => "Preview indisponible";
        public Task<StudioRuntimeResult> StartPreviewAsync(SceneDefinition scene, CancellationToken cancellationToken) => Failure();
        public Task<StudioRuntimeResult> StopPreviewAsync(Guid sceneId, CancellationToken cancellationToken) => Failure();
        private static Task<StudioRuntimeResult> Failure() =>
            Task.FromResult(StudioRuntimeResult.Unavailable("Preview indisponible"));
    }

    private sealed class FakeStreamingRuntime : IStreamingRuntime
    {
        public bool IsAvailable => true;
        public string UnavailableMessage => "";
        public event EventHandler<StreamingStateChangedEventArgs>? StreamingStateChanged
        {
            add { }
            remove { }
        }
        public Task<StudioRuntimeResult> StartStreamingAsync(
            StreamingRequest request,
            CancellationToken cancellationToken) => Task.FromResult(StudioRuntimeResult.Success());
        public Task<StudioRuntimeResult> StopStreamingAsync(CancellationToken cancellationToken) =>
            Task.FromResult(StudioRuntimeResult.Success());

        public StudioRuntimeResult SwitchStreamingScene(Guid sceneId) =>
            StudioRuntimeResult.Success();
    }

    private sealed class FakeProviderStore : IProviderStore
    {
        public event EventHandler? Changed
        {
            add { }
            remove { }
        }
        public IReadOnlyCollection<ProviderSettings> GetAll() => [];
        public ProviderSettings? Get(string providerId) => null;
        public void Save(ProviderSettings provider) { }
        public void Delete(string providerId) { }
    }
}
