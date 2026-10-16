using CastorApplication.Models.Settings;
using CastorApplication.Models.Settings.Providers;
using CastorApplication.Models.Studio;
using CastorApplication.Services.Auth.Storage;
using CastorApplication.Services.Settings;
using CastorApplication.Services.Studio;
using CastorApplication.ViewModels.Multicam;
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
        public RecordingRequest? Request { get; private set; }
        public StudioRuntimeResult StartResult { get; init; } = StudioRuntimeResult.Success();

        public event EventHandler<RecordingStateChangedEventArgs>? StateChanged;

        public Task<StudioRuntimeResult> StartRecordingAsync(RecordingRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(StartResult);
        }

        public Task<StudioRuntimeResult> StopRecordingAsync(CancellationToken cancellationToken) =>
            Task.FromResult(StudioRuntimeResult.Success());

        public List<Guid> SwitchedScenes { get; } = [];
        public StudioRuntimeResult SwitchResult { get; set; } = StudioRuntimeResult.Success();

        public StudioRuntimeResult SwitchRecordingScene(Guid sceneId)
        {
            SwitchedScenes.Add(sceneId);
            return SwitchResult;
        }

        public void RaiseState(RecordingState state, string message = "") =>
            StateChanged?.Invoke(this, new RecordingStateChangedEventArgs(state, message));

        public string PauseUnavailableReason { get; set; } = "";
        public StudioRuntimeResult PauseResult { get; set; } = StudioRuntimeResult.Success();
        public StudioRuntimeResult ResumeResult { get; set; } = StudioRuntimeResult.Success();
        public int PauseCalls { get; private set; }
        public int ResumeCalls { get; private set; }

        public StudioRuntimeResult PauseRecording()
        {
            PauseCalls++;
            return PauseResult;
        }

        public StudioRuntimeResult ResumeRecording()
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

    private sealed class RecordingFixture(
        string directory,
        StudioWorkspaceViewModel workspace,
        FakeRecordingRuntime runtime,
        StudioViewModel viewModel,
        ManualTimeProvider time) : IDisposable
    {
        public StudioWorkspaceViewModel Workspace { get; } = workspace;
        public FakeRecordingRuntime Runtime { get; } = runtime;
        public StudioViewModel ViewModel { get; } = viewModel;
        public ManualTimeProvider Time { get; } = time;

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }

    private static RecordingFixture CreateRecordingFixture(FakeRecordingRuntime? runtime = null)
    {
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
            timeProvider: time
        );
        return new RecordingFixture(directory, workspace, runtime, viewModel, time);
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
