using CastorApplication.Models.Settings;
using CastorApplication.Models.Settings.Providers;
using CastorApplication.Models.Studio;
using CastorApplication.Services.Auth.Storage;
using CastorApplication.Services.Settings;
using CastorApplication.Services.Studio;
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
                workspace, new FakeStudioRuntime(), recordingRuntime, new FakeProviderStore(), settingsService);

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
                workspace, new FakeStudioRuntime(), recordingRuntime, new FakeProviderStore(), settingsService);

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
                workspace, new FakeStudioRuntime(), recordingRuntime, new FakeProviderStore(), settingsService);

            await viewModel.StartRecordingCommand.ExecuteAsync(null);

            Assert.False(workspace.IsRecording);
            Assert.Equal("échec output", viewModel.RecordError);
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
                workspace, new FakeStudioRuntime(), recordingRuntime, new FakeProviderStore(), settingsService);
            await viewModel.StartRecordingCommand.ExecuteAsync(null);

            recordingRuntime.RaiseState(false, "Disque plein");

            Assert.False(workspace.IsRecording);
            Assert.Equal("Disque plein", viewModel.RecordError);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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

        public void RaiseState(bool isRecording, string message = "") =>
            StateChanged?.Invoke(this, new RecordingStateChangedEventArgs(isRecording, message));
    }

    private sealed class FakeStudioRuntime : IStudioRuntime
    {
        public bool IsAvailable => false;
        public string UnavailableMessage => "Preview indisponible";
        public Task<StudioRuntimeResult> StartPreviewAsync(SceneDefinition scene, CancellationToken cancellationToken) => Failure();
        public Task<StudioRuntimeResult> StopPreviewAsync(Guid sceneId, CancellationToken cancellationToken) => Failure();
        public Task<StudioRuntimeResult> StartStreamingAsync(StreamingRequest request, CancellationToken cancellationToken) => Failure();
        public Task<StudioRuntimeResult> StopStreamingAsync(CancellationToken cancellationToken) => Failure();
        private static Task<StudioRuntimeResult> Failure() =>
            Task.FromResult(StudioRuntimeResult.Unavailable("Preview indisponible"));
    }

    private sealed class FakeProviderStore : IProviderStore
    {
        public IReadOnlyCollection<ProviderSettings> GetAll() => [];
        public ProviderSettings? Get(string providerId) => null;
        public void Save(ProviderSettings provider) { }
        public void Delete(string providerId) { }
    }
}
