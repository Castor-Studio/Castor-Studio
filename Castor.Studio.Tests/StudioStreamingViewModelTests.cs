using CastorApplication.Models.Settings;
using CastorApplication.Models.Settings.Providers;
using CastorApplication.Models.Studio;
using CastorApplication.Services.Auth.Storage;
using CastorApplication.Services.Settings;
using CastorApplication.Services.Studio;
using CastorApplication.ViewModels.Scenes;
using CastorApplication.ViewModels.Studio;

namespace Castor.Studio.Tests;

public sealed class StudioStreamingViewModelTests
{
    [Fact]
    public async Task Start_live_uses_the_connected_twitch_account_and_stream_settings()
    {
        var fixture = CreateFixture(new ProviderSettings
        {
            ProviderId = "twitch",
            IsConnected = true,
            UserName = "Castor",
            StreamKey = "live-key"
        }, new ApplicationSettings
        {
            SelectedFpsIndex = 2,
            StreamingBitrate = 5_500,
            SelectedAudioBitrateIndex = 0,
            SelectedSampleRateIndex = 1,
            SelectedChannelsIndex = 1
        });

        try
        {
            await fixture.ViewModel.StartStreamingCommand.ExecuteAsync(null);

            var request = Assert.IsType<StreamingRequest>(fixture.StreamingRuntime.Request);
            Assert.Equal(fixture.Scene.Id, request.Scene.Id);
            Assert.Equal("live-key", request.StreamKey);
            Assert.Equal(25, request.Fps);
            Assert.Equal(5_500, request.VideoBitrateKbps);
            Assert.Equal(320, request.AudioBitrateKbps);
            Assert.Equal(44_100, request.AudioSampleRate);
            Assert.Equal(1, request.AudioChannels);
            Assert.True(fixture.Workspace.IsStreaming);
            Assert.Equal("Connecté en tant que Castor", fixture.ViewModel.ConnectedAccountLabel);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Theory]
    [InlineData(false, "live-key")]
    [InlineData(true, "")]
    public async Task Start_live_requires_a_connected_account_with_a_stream_key(
        bool isConnected,
        string streamKey)
    {
        var fixture = CreateFixture(new ProviderSettings
        {
            ProviderId = "twitch",
            IsConnected = isConnected,
            UserName = "Castor",
            StreamKey = streamKey
        });

        try
        {
            await fixture.ViewModel.StartStreamingCommand.ExecuteAsync(null);

            Assert.Null(fixture.StreamingRuntime.Request);
            Assert.False(fixture.Workspace.IsStreaming);
            Assert.Contains("Compte Twitch déconnecté", fixture.ViewModel.StreamError);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task Runtime_failure_does_not_mark_the_workspace_live()
    {
        var fixture = CreateFixture(ConnectedProvider());
        fixture.StreamingRuntime.StartResult = StudioRuntimeResult.Failure("Connexion refusée");

        try
        {
            await fixture.ViewModel.StartStreamingCommand.ExecuteAsync(null);

            Assert.False(fixture.Workspace.IsStreaming);
            Assert.Equal("Connexion refusée", fixture.ViewModel.StreamError);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task Stop_live_updates_state_only_when_the_runtime_succeeds()
    {
        var fixture = CreateFixture(ConnectedProvider());
        try
        {
            fixture.Workspace.SetStreamingState(true);
            fixture.StreamingRuntime.StopResult = StudioRuntimeResult.Failure("Arrêt impossible");

            await fixture.ViewModel.StopStreamingCommand.ExecuteAsync(null);

            Assert.True(fixture.Workspace.IsStreaming);
            Assert.Equal("Arrêt impossible", fixture.ViewModel.StreamError);

            fixture.StreamingRuntime.StopResult = StudioRuntimeResult.Success();
            await fixture.ViewModel.StopStreamingCommand.ExecuteAsync(null);

            Assert.False(fixture.Workspace.IsStreaming);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public void Switching_scene_while_streaming_re_points_the_output()
    {
        var fixture = CreateFixture(ConnectedProvider());
        try
        {
            var second = fixture.Workspace.CreateScene("Autre scène");
            fixture.Workspace.AddSource(second, new SourceDefinition { Name = "Webcam", Kind = SourceKind.Video });
            fixture.Workspace.SetStreamingState(true);

            fixture.Workspace.SelectScene(second);

            Assert.Equal([second.Id], fixture.StreamingRuntime.SwitchedScenes);
            Assert.Equal("", fixture.ViewModel.StreamError);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public void Refused_streaming_scene_switch_surfaces_the_native_message()
    {
        var fixture = CreateFixture(ConnectedProvider());
        try
        {
            var second = fixture.Workspace.CreateScene("Autre scène");
            fixture.Workspace.AddSource(second, new SourceDefinition { Name = "Webcam", Kind = SourceKind.Video });
            fixture.Workspace.SetStreamingState(true);
            fixture.StreamingRuntime.SwitchResult = StudioRuntimeResult.Failure("scène refusée");

            fixture.Workspace.SelectScene(second);

            Assert.Equal("scène refusée", fixture.ViewModel.StreamError);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task Unexpected_native_stop_updates_the_ui_and_reports_the_error()
    {
        var fixture = CreateFixture(ConnectedProvider());
        try
        {
            await fixture.ViewModel.StartStreamingCommand.ExecuteAsync(null);

            fixture.StreamingRuntime.RaiseState(false, "Connexion Twitch interrompue");

            Assert.False(fixture.Workspace.IsStreaming);
            Assert.Equal("Connexion Twitch interrompue", fixture.ViewModel.StreamError);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task Recording_and_live_cannot_start_at_the_same_time()
    {
        var fixture = CreateFixture(ConnectedProvider());
        try
        {
            fixture.Workspace.SetRecordingState(true);
            await fixture.ViewModel.StartStreamingCommand.ExecuteAsync(null);

            Assert.Null(fixture.StreamingRuntime.Request);
            Assert.Contains("Arrêtez l'enregistrement", fixture.ViewModel.StreamError);

            fixture.Workspace.SetRecordingState(false);
            fixture.Workspace.SetStreamingState(true);
            await fixture.ViewModel.StartRecordingCommand.ExecuteAsync(null);

            Assert.Null(fixture.RecordingRuntime.Request);
            Assert.Contains("Arrêtez le live", fixture.ViewModel.RecordError);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public void Account_status_refreshes_when_the_provider_store_changes()
    {
        var fixture = CreateFixture(null);
        try
        {
            Assert.Equal("Compte Twitch non connecté", fixture.ViewModel.ConnectedAccountLabel);

            fixture.ProviderStore.Save(ConnectedProvider());

            Assert.Equal("Connecté en tant que Castor", fixture.ViewModel.ConnectedAccountLabel);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task Without_an_account_start_is_disabled_with_its_reason_and_the_panel_leads_to_the_accounts()
    {
        var fixture = CreateFixture(null);
        try
        {
            var viewModel = fixture.ViewModel;
            Assert.False(viewModel.IsAccountConnected);
            Assert.False(viewModel.StartStreamingCommand.CanExecute(null));
            Assert.Equal("Connectez un compte Twitch pour lancer le live.", viewModel.StartStreamingBlockedReason);
            Assert.Equal("Twitch", viewModel.DestinationLabel);

            var requested = 0;
            viewModel.AccountSettingsRequested += (_, _) => requested++;
            viewModel.OpenAccountSettingsCommand.Execute(null);
            Assert.Equal(1, requested);

            // An earlier failed attempt left the account error behind.
            await viewModel.StartStreamingCommand.ExecuteAsync(null);
            Assert.Contains("Compte Twitch déconnecté", viewModel.StreamError);

            // Back from Settings with an account connected.
            fixture.ProviderStore.Save(ConnectedProvider());

            Assert.True(viewModel.StartStreamingCommand.CanExecute(null));
            Assert.Equal("", viewModel.StartStreamingBlockedReason);
            Assert.Equal("Twitch · Castor", viewModel.DestinationLabel);
            Assert.Equal("", viewModel.StreamError);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private static ProviderSettings ConnectedProvider() => new()
    {
        ProviderId = "twitch",
        IsConnected = true,
        UserName = "Castor",
        StreamKey = "live-key"
    };

    private static Fixture CreateFixture(
        ProviderSettings? provider,
        ApplicationSettings? settings = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"castor-stream-viewmodel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var settingsService = new SettingsService(Path.Combine(directory, "settings.json"));
        settingsService.Save(settings ?? new ApplicationSettings());
        var workspace = new StudioWorkspaceViewModel();
        var scene = workspace.CreateScene("Live Twitch");
        workspace.AddSource(scene, new SourceDefinition { Name = "Écran", Kind = SourceKind.Video });
        var streamingRuntime = new FakeStreamingRuntime();
        var recordingRuntime = new FakeRecordingRuntime();
        var providerStore = new FakeProviderStore(provider);
        var viewModel = new StudioViewModel(
            workspace,
            new FakeStudioRuntime(),
            new UnavailableScenePreviewRuntime(),
            recordingRuntime,
            streamingRuntime,
            providerStore,
            settingsService);
        return new Fixture(
            directory,
            workspace,
            scene,
            viewModel,
            streamingRuntime,
            recordingRuntime,
            providerStore);
    }

    private sealed record Fixture(
        string Directory,
        StudioWorkspaceViewModel Workspace,
        SceneItemViewModel Scene,
        StudioViewModel ViewModel,
        FakeStreamingRuntime StreamingRuntime,
        FakeRecordingRuntime RecordingRuntime,
        FakeProviderStore ProviderStore) : IDisposable
    {
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }

    private sealed class FakeStreamingRuntime : IStreamingRuntime
    {
        public bool IsAvailable => true;
        public string UnavailableMessage => "";
        public StreamingRequest? Request { get; private set; }
        public StudioRuntimeResult StartResult { get; set; } = StudioRuntimeResult.Success();
        public StudioRuntimeResult StopResult { get; set; } = StudioRuntimeResult.Success();
        public List<Guid> SwitchedScenes { get; } = [];
        public StudioRuntimeResult SwitchResult { get; set; } = StudioRuntimeResult.Success();
        public event EventHandler<StreamingStateChangedEventArgs>? StreamingStateChanged;

        public Task<StudioRuntimeResult> StartStreamingAsync(
            StreamingRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(StartResult);
        }

        public Task<StudioRuntimeResult> StopStreamingAsync(CancellationToken cancellationToken) =>
            Task.FromResult(StopResult);

        public StudioRuntimeResult SwitchStreamingScene(Guid sceneId)
        {
            SwitchedScenes.Add(sceneId);
            return SwitchResult;
        }

        public void RaiseState(bool isStreaming, string message = "") =>
            StreamingStateChanged?.Invoke(this, new StreamingStateChangedEventArgs(isStreaming, message));
    }

    private sealed class FakeRecordingRuntime : IRecordingRuntime
    {
        public bool IsAvailable => true;
        public string UnavailableMessage => "";
        public RecordingRequest? Request { get; private set; }
        public event EventHandler<RecordingStateChangedEventArgs>? StateChanged
        {
            add { }
            remove { }
        }

        public Task<StudioRuntimeResult> StartRecordingAsync(
            RecordingRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(StudioRuntimeResult.Success());
        }

        public Task<StudioRuntimeResult> StopRecordingAsync(CancellationToken cancellationToken) =>
            Task.FromResult(StudioRuntimeResult.Success());

        public StudioRuntimeResult SwitchRecordingScene(Guid sceneId) =>
            StudioRuntimeResult.Success();
    }

    private sealed class FakeProviderStore(ProviderSettings? provider) : IProviderStore
    {
        private ProviderSettings? _provider = provider;
        public event EventHandler? Changed;
        public IReadOnlyCollection<ProviderSettings> GetAll() => _provider == null ? [] : [_provider];
        public ProviderSettings? Get(string providerId) =>
            _provider?.ProviderId == providerId ? _provider : null;
        public void Save(ProviderSettings next)
        {
            _provider = next;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        public void Delete(string providerId)
        {
            if (_provider?.ProviderId == providerId) _provider = null;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakeStudioRuntime : IStudioRuntime
    {
        public bool IsAvailable => false;
        public string UnavailableMessage => "Preview indisponible";
        public Task<StudioRuntimeResult> StartPreviewAsync(
            SceneDefinition scene,
            CancellationToken cancellationToken) => Unavailable();
        public Task<StudioRuntimeResult> StopPreviewAsync(Guid sceneId, CancellationToken cancellationToken) => Unavailable();
        private static Task<StudioRuntimeResult> Unavailable() =>
            Task.FromResult(StudioRuntimeResult.Unavailable("Preview indisponible"));
    }
}
