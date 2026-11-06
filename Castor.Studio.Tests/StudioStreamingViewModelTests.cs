using CastorApplication.Models.Settings;
using CastorApplication.Models.Settings.Providers;
using CastorApplication.Models.Studio;
using CastorApplication.Services.Auth.Storage;
using CastorApplication.Services.Settings;
using CastorApplication.Services.Streaming;
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
            Assert.Equal(new StreamingDestination("twitch", "", "live-key"), Assert.Single(request.Destinations));
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
            Assert.Contains("Compte de streaming déconnecté", fixture.ViewModel.StreamError.Text);
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
            Assert.Equal("Connexion refusée", fixture.ViewModel.StreamError.Text);
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
            Assert.Equal("Arrêt impossible", fixture.ViewModel.StreamError.Text);

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
            Assert.Equal("", fixture.ViewModel.StreamError.Text);
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

            Assert.Equal("scène refusée", fixture.ViewModel.StreamError.Text);
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
            Assert.Equal("Connexion Twitch interrompue", fixture.ViewModel.StreamError.Text);
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
            fixture.Workspace.TrySetRecordingState(RecordingState.Recording);
            await fixture.ViewModel.StartStreamingCommand.ExecuteAsync(null);

            Assert.Null(fixture.StreamingRuntime.Request);
            Assert.Contains("Arrêtez l'enregistrement", fixture.ViewModel.StreamError.Text);

            fixture.Workspace.TrySetRecordingState(RecordingState.Stopping);
            fixture.Workspace.TrySetRecordingState(RecordingState.Idle);
            fixture.Workspace.SetStreamingState(true);
            await fixture.ViewModel.StartRecordingCommand.ExecuteAsync(null);

            Assert.Null(fixture.RecordingRuntime.Request);
            Assert.Contains("Arrêtez le live", fixture.ViewModel.RecordError.Text);
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
            Assert.Equal("Aucun compte de streaming connecté", fixture.ViewModel.ConnectedAccountLabel);

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
            Assert.Equal("Connectez un compte Twitch ou YouTube pour lancer le live.", viewModel.StartStreamingBlockedReason);
            Assert.Equal("Twitch ou YouTube", viewModel.DestinationLabel);

            var requested = 0;
            viewModel.AccountSettingsRequested += (_, _) => requested++;
            viewModel.OpenAccountSettingsCommand.Execute(null);
            Assert.Equal(1, requested);

            // An earlier failed attempt left the account error behind.
            await viewModel.StartStreamingCommand.ExecuteAsync(null);
            Assert.Contains("Compte de streaming déconnecté", viewModel.StreamError.Text);

            // Back from Settings with an account connected.
            fixture.ProviderStore.Save(ConnectedProvider());

            Assert.True(viewModel.StartStreamingCommand.CanExecute(null));
            Assert.Equal("", viewModel.StartStreamingBlockedReason);
            Assert.Equal("Twitch · Castor", viewModel.DestinationLabel);
            Assert.Equal("", viewModel.StreamError.Text);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task Start_live_pushes_to_the_youtube_ingest_server()
    {
        var fixture = CreateFixture(ConnectedYouTube());
        try
        {
            Assert.Equal("YouTube · Club TV", fixture.ViewModel.DestinationLabel);

            await fixture.ViewModel.StartStreamingCommand.ExecuteAsync(null);

            var request = Assert.IsType<StreamingRequest>(fixture.StreamingRuntime.Request);
            Assert.Equal(
                new StreamingDestination("youtube", "rtmp://a.rtmp.youtube.com/live2", "yt-key"),
                Assert.Single(request.Destinations));
            Assert.True(fixture.Workspace.IsStreaming);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public void Youtube_account_without_ingest_server_cannot_go_live()
    {
        var account = ConnectedYouTube();
        account.IngestUrl = "";
        var fixture = CreateFixture(account);
        try
        {
            Assert.False(fixture.ViewModel.IsAccountConnected);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Theory]
    [InlineData("", "twitch")]
    [InlineData("twitch", "twitch")]
    [InlineData("youtube", "youtube")]
    public async Task With_two_accounts_the_setting_picks_the_destination(string chosen, string expected)
    {
        var fixture = CreateFixture(
            ConnectedProvider(),
            new ApplicationSettings { StreamingProviderIds = chosen.Length == 0 ? [] : [chosen] },
            others: [ConnectedYouTube()]);
        try
        {
            await fixture.ViewModel.StartStreamingCommand.ExecuteAsync(null);

            Assert.Equal(expected, fixture.StreamingRuntime.Request?.Destinations.Single().ProviderId);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public void A_chosen_destination_that_is_disconnected_falls_back_to_the_connected_one()
    {
        var fixture = CreateFixture(ConnectedProvider(), new ApplicationSettings { StreamingProviderIds = ["youtube"] });
        try
        {
            Assert.Equal("Twitch · Castor", fixture.ViewModel.DestinationLabel);

            fixture.ProviderStore.Save(ConnectedYouTube());

            Assert.Equal("YouTube · Club TV", fixture.ViewModel.DestinationLabel);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private static Fixture CreateMultistreamFixture(FakeBroadcastService? broadcasts = null) => CreateFixture(
        ConnectedProvider(),
        new ApplicationSettings { StreamingProviderIds = ["twitch", "youtube"] },
        broadcasts,
        ConnectedYouTube());

    // "" when the platform is not live, or live without a public page.
    private static string WatchUrlOf(Fixture fixture, string providerId) =>
        fixture.ViewModel.LivePlatforms.FirstOrDefault(platform => platform.ProviderId == providerId)?.WatchUrl ?? "";

    [Fact]
    public async Task Each_live_platform_is_listed_with_its_account_and_page()
    {
        var fixture = CreateMultistreamFixture(BroadcastsCreating(YouTubeBroadcast()));
        try
        {
            await fixture.ViewModel.StartStreamingCommand.ExecuteAsync(null);

            Assert.Equal(["Twitch", "YouTube"], fixture.ViewModel.LivePlatforms.Select(platform => platform.Name));
            Assert.Equal(["Castor", "Club TV"], fixture.ViewModel.LivePlatforms.Select(platform => platform.AccountName));
            Assert.Equal([false, true], fixture.ViewModel.LivePlatforms.Select(platform => platform.HasWatchUrl));
            Assert.Equal("2 lives en cours", fixture.ViewModel.LivePlatformsToolTip);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task Stopping_one_platform_leaves_the_other_live()
    {
        var broadcast = YouTubeBroadcast();
        var fixture = CreateMultistreamFixture(BroadcastsCreating(broadcast));
        try
        {
            await fixture.ViewModel.StartStreamingCommand.ExecuteAsync(null);
            var youTube = fixture.ViewModel.LivePlatforms.Single(platform => platform.ProviderId == "youtube");

            await fixture.ViewModel.StopLivePlatformCommand.ExecuteAsync(youTube);

            Assert.Equal(["youtube"], fixture.StreamingRuntime.StoppedProviders);
            Assert.Equal([broadcast], fixture.Broadcasts.Ended);
            Assert.True(fixture.Workspace.IsStreaming);
            Assert.Equal("twitch", Assert.Single(fixture.ViewModel.LivePlatforms).ProviderId);
            Assert.Equal("Live en cours sur Twitch", fixture.ViewModel.LivePlatformsToolTip);

            await fixture.ViewModel.StopLivePlatformCommand.ExecuteAsync(fixture.ViewModel.LivePlatforms[0]);

            Assert.False(fixture.Workspace.IsStreaming);
            Assert.Empty(fixture.ViewModel.LivePlatforms);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task A_refused_platform_stop_keeps_it_listed()
    {
        var fixture = CreateMultistreamFixture();
        try
        {
            await fixture.ViewModel.StartStreamingCommand.ExecuteAsync(null);
            fixture.StreamingRuntime.StopResult = StudioRuntimeResult.Failure("Arrêt du live YouTube impossible : erreur");

            await fixture.ViewModel.StopLivePlatformCommand.ExecuteAsync(fixture.ViewModel.LivePlatforms[1]);

            Assert.Equal(2, fixture.ViewModel.LivePlatforms.Count);
            Assert.Equal("Arrêt du live YouTube impossible : erreur", fixture.ViewModel.StreamError.Text);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task Both_chosen_platforms_go_live_from_one_start()
    {
        var broadcast = YouTubeBroadcast();
        var fixture = CreateMultistreamFixture(BroadcastsCreating(broadcast));
        try
        {
            Assert.Equal("Twitch · Castor + YouTube · Club TV", fixture.ViewModel.DestinationLabel);
            Assert.Equal("Connecté en tant que Castor, Club TV", fixture.ViewModel.ConnectedAccountLabel);

            await fixture.ViewModel.StartStreamingCommand.ExecuteAsync(null);

            var request = Assert.IsType<StreamingRequest>(fixture.StreamingRuntime.Request);
            Assert.Equal(["twitch", "youtube"], request.Destinations.Select(destination => destination.ProviderId));
            Assert.Equal(["twitch", "youtube"], fixture.Broadcasts.Prepared);
            Assert.True(fixture.Workspace.IsStreaming);
            Assert.Equal(broadcast.WatchUrl, WatchUrlOf(fixture, "youtube"));
            Assert.Equal("", fixture.ViewModel.StreamError.Text);

            await fixture.ViewModel.StopStreamingCommand.ExecuteAsync(null);

            Assert.False(fixture.Workspace.IsStreaming);
            Assert.Equal([broadcast], fixture.Broadcasts.Ended);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task A_platform_refused_at_start_leaves_the_other_live()
    {
        var broadcast = YouTubeBroadcast();
        var fixture = CreateMultistreamFixture(BroadcastsCreating(broadcast));
        fixture.StreamingRuntime.StartResults["youtube"] = StudioRuntimeResult.Failure("Connexion refusée.");
        try
        {
            await fixture.ViewModel.StartStreamingCommand.ExecuteAsync(null);

            Assert.True(fixture.Workspace.IsStreaming);
            Assert.Equal([broadcast], fixture.Broadcasts.Ended);
            Assert.Equal("", WatchUrlOf(fixture, "youtube"));
            Assert.Equal(
                $"YouTube : Connexion refusée.{Environment.NewLine}Le live continue sur Twitch.",
                fixture.ViewModel.StreamError.Text);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task A_failed_broadcast_leaves_its_platform_out_of_the_live()
    {
        var broadcasts = new FakeBroadcastService();
        broadcasts.PrepareResults["youtube"] = new LiveBroadcastResult(
            StudioRuntimeResult.Failure("Le quota de l'API YouTube est épuisé."));
        var fixture = CreateMultistreamFixture(broadcasts);
        try
        {
            await fixture.ViewModel.StartStreamingCommand.ExecuteAsync(null);

            var request = Assert.IsType<StreamingRequest>(fixture.StreamingRuntime.Request);
            Assert.Equal("twitch", Assert.Single(request.Destinations).ProviderId);
            Assert.True(fixture.Workspace.IsStreaming);
            Assert.Equal(
                $"Le quota de l'API YouTube est épuisé.{Environment.NewLine}Le live continue sur Twitch.",
                fixture.ViewModel.StreamError.Text);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task Every_platform_failing_keeps_the_studio_off_air()
    {
        var fixture = CreateMultistreamFixture();
        fixture.StreamingRuntime.StartResult = StudioRuntimeResult.Failure("Cette scène n'existe pas dans LibObs.");
        try
        {
            await fixture.ViewModel.StartStreamingCommand.ExecuteAsync(null);

            Assert.False(fixture.Workspace.IsStreaming);
            Assert.Equal(
                $"Twitch : Cette scène n'existe pas dans LibObs.{Environment.NewLine}YouTube : Cette scène n'existe pas dans LibObs.",
                fixture.ViewModel.StreamError.Text);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task One_live_dropping_ends_its_broadcast_and_keeps_the_studio_on_air()
    {
        var broadcast = YouTubeBroadcast();
        var fixture = CreateMultistreamFixture(BroadcastsCreating(broadcast));
        try
        {
            await fixture.ViewModel.StartStreamingCommand.ExecuteAsync(null);

            fixture.StreamingRuntime.RaiseState(false, "La connexion au serveur YouTube a été interrompue.", "youtube");

            Assert.True(fixture.Workspace.IsStreaming);
            Assert.Equal([broadcast], fixture.Broadcasts.Ended);
            Assert.Equal("twitch", Assert.Single(fixture.ViewModel.LivePlatforms).ProviderId);
            Assert.Equal(
                $"La connexion au serveur YouTube a été interrompue.{Environment.NewLine}Le live continue sur Twitch.",
                fixture.ViewModel.StreamError.Text);

            fixture.StreamingRuntime.RaiseState(false, "", "twitch");

            Assert.False(fixture.Workspace.IsStreaming);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task Chosen_platforms_that_cannot_go_live_are_skipped()
    {
        var youTube = ConnectedYouTube();
        youTube.IngestUrl = "";
        var fixture = CreateFixture(
            ConnectedProvider(),
            new ApplicationSettings { StreamingProviderIds = ["twitch", "youtube"] },
            others: [youTube]);
        try
        {
            Assert.Equal("Twitch · Castor", fixture.ViewModel.DestinationLabel);

            await fixture.ViewModel.StartStreamingCommand.ExecuteAsync(null);

            Assert.Equal("twitch", Assert.Single(fixture.StreamingRuntime.Request!.Destinations).ProviderId);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private static LiveBroadcast YouTubeBroadcast() =>
        new("youtube", "b1", "https://www.youtube.com/watch?v=b1");

    // Only the broadcast's own platform gets it; the others prepare nothing, as Twitch does.
    private static FakeBroadcastService BroadcastsCreating(LiveBroadcast broadcast)
    {
        var broadcasts = new FakeBroadcastService();
        broadcasts.PrepareResults[broadcast.ProviderId] = new LiveBroadcastResult(StudioRuntimeResult.Success(), broadcast);
        return broadcasts;
    }

    [Fact]
    public async Task Live_broadcast_is_prepared_before_the_output_and_ended_after_it()
    {
        var broadcast = YouTubeBroadcast();
        var fixture = CreateFixture(ConnectedYouTube(), broadcasts: BroadcastsCreating(broadcast));
        try
        {
            await fixture.ViewModel.StartStreamingCommand.ExecuteAsync(null);

            Assert.Equal(["youtube"], fixture.Broadcasts.Prepared);
            Assert.NotNull(fixture.StreamingRuntime.Request);
            Assert.Equal("https://www.youtube.com/watch?v=b1", WatchUrlOf(fixture, "youtube"));
            Assert.True(Assert.Single(fixture.ViewModel.LivePlatforms).HasWatchUrl);
            Assert.Empty(fixture.Broadcasts.Ended);

            await fixture.ViewModel.StopStreamingCommand.ExecuteAsync(null);
            // The runtime also signals the stop: the broadcast still ends once.
            fixture.StreamingRuntime.RaiseState(false);

            Assert.Equal([broadcast], fixture.Broadcasts.Ended);
            Assert.Empty(fixture.ViewModel.LivePlatforms);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task Failed_broadcast_keeps_the_output_from_starting()
    {
        var broadcasts = new FakeBroadcastService
        {
            PrepareResult = new LiveBroadcastResult(StudioRuntimeResult.Failure("Le quota de l'API YouTube est épuisé."))
        };
        var fixture = CreateFixture(ConnectedYouTube(), broadcasts: broadcasts);
        try
        {
            await fixture.ViewModel.StartStreamingCommand.ExecuteAsync(null);

            Assert.Null(fixture.StreamingRuntime.Request);
            Assert.False(fixture.Workspace.IsStreaming);
            Assert.Equal("Le quota de l'API YouTube est épuisé.", fixture.ViewModel.StreamError.Text);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task Output_that_fails_to_start_ends_its_broadcast()
    {
        var broadcast = YouTubeBroadcast();
        var fixture = CreateFixture(ConnectedYouTube(), broadcasts: BroadcastsCreating(broadcast));
        fixture.StreamingRuntime.StartResult = StudioRuntimeResult.Failure("YouTube a refusé la clé de stream.");
        try
        {
            await fixture.ViewModel.StartStreamingCommand.ExecuteAsync(null);

            Assert.Equal([broadcast], fixture.Broadcasts.Ended);
            Assert.False(fixture.Workspace.IsStreaming);
            Assert.Empty(fixture.ViewModel.LivePlatforms);
            Assert.Equal("YouTube a refusé la clé de stream.", fixture.ViewModel.StreamError.Text);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task Unexpected_stop_ends_the_broadcast()
    {
        var broadcast = YouTubeBroadcast();
        var fixture = CreateFixture(ConnectedYouTube(), broadcasts: BroadcastsCreating(broadcast));
        try
        {
            await fixture.ViewModel.StartStreamingCommand.ExecuteAsync(null);

            fixture.StreamingRuntime.RaiseState(false, "La connexion au serveur YouTube a été interrompue.");

            Assert.Equal([broadcast], fixture.Broadcasts.Ended);
            Assert.False(fixture.Workspace.IsStreaming);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task A_broadcast_youtube_could_not_end_is_reported_after_the_stop()
    {
        var fixture = CreateFixture(ConnectedYouTube(), broadcasts: BroadcastsCreating(YouTubeBroadcast()));
        fixture.Broadcasts.EndResult = StudioRuntimeResult.Failure("Le live est arrêté, mais YouTube n'a pas pu le terminer.");
        try
        {
            await fixture.ViewModel.StartStreamingCommand.ExecuteAsync(null);
            await fixture.ViewModel.StopStreamingCommand.ExecuteAsync(null);

            Assert.False(fixture.Workspace.IsStreaming);
            Assert.Equal("Le live est arrêté, mais YouTube n'a pas pu le terminer.", fixture.ViewModel.StreamError.Text);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task Twitch_lives_have_no_watch_page()
    {
        var fixture = CreateFixture(ConnectedProvider());
        try
        {
            await fixture.ViewModel.StartStreamingCommand.ExecuteAsync(null);

            Assert.Equal(["twitch"], fixture.Broadcasts.Prepared);
            Assert.False(Assert.Single(fixture.ViewModel.LivePlatforms).HasWatchUrl);
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

    private static ProviderSettings ConnectedYouTube() => new()
    {
        ProviderId = "youtube",
        IsConnected = true,
        UserName = "Club TV",
        StreamKey = "yt-key",
        IngestUrl = "rtmp://a.rtmp.youtube.com/live2",
        StreamId = "stream-1"
    };

    private static Fixture CreateFixture(
        ProviderSettings? provider,
        ApplicationSettings? settings = null,
        FakeBroadcastService? broadcasts = null,
        params ProviderSettings[] others)
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
        var providerStore = new FakeProviderStore(provider == null ? others : [provider, .. others]);
        broadcasts ??= new FakeBroadcastService();
        var viewModel = new StudioViewModel(
            workspace,
            new FakeStudioRuntime(),
            new UnavailableScenePreviewRuntime(),
            recordingRuntime,
            streamingRuntime,
            providerStore,
            settingsService,
            broadcasts: broadcasts);
        return new Fixture(
            directory,
            workspace,
            scene,
            viewModel,
            streamingRuntime,
            recordingRuntime,
            providerStore,
            broadcasts);
    }

    private sealed record Fixture(
        string Directory,
        StudioWorkspaceViewModel Workspace,
        SceneItemViewModel Scene,
        StudioViewModel ViewModel,
        FakeStreamingRuntime StreamingRuntime,
        FakeRecordingRuntime RecordingRuntime,
        FakeProviderStore ProviderStore,
        FakeBroadcastService Broadcasts) : IDisposable
    {
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }

    private sealed class FakeBroadcastService : ILiveBroadcastService
    {
        public LiveBroadcastResult PrepareResult { get; set; } = LiveBroadcastResult.None;
        // Per platform, over PrepareResult.
        public Dictionary<string, LiveBroadcastResult> PrepareResults { get; } = [];
        public StudioRuntimeResult EndResult { get; set; } = StudioRuntimeResult.Success();
        public List<string> Prepared { get; } = [];
        public List<LiveBroadcast> Ended { get; } = [];

        public Task<LiveBroadcastResult> PrepareAsync(
            ProviderSettings account,
            ApplicationSettings settings,
            CancellationToken cancellationToken)
        {
            Prepared.Add(account.ProviderId);
            return Task.FromResult(PrepareResults.GetValueOrDefault(account.ProviderId, PrepareResult));
        }

        public Task<StudioRuntimeResult> EndAsync(LiveBroadcast broadcast, CancellationToken cancellationToken)
        {
            Ended.Add(broadcast);
            return Task.FromResult(EndResult);
        }
    }

    private sealed class FakeStreamingRuntime : IStreamingRuntime
    {
        public bool IsAvailable => true;
        public string UnavailableMessage => "";
        public StreamingRequest? Request { get; private set; }
        public StudioRuntimeResult StartResult { get; set; } = StudioRuntimeResult.Success();
        // Per platform, over StartResult.
        public Dictionary<string, StudioRuntimeResult> StartResults { get; } = [];
        public StudioRuntimeResult StopResult { get; set; } = StudioRuntimeResult.Success();
        public List<Guid> SwitchedScenes { get; } = [];
        public StudioRuntimeResult SwitchResult { get; set; } = StudioRuntimeResult.Success();
        public event EventHandler<StreamingStateChangedEventArgs>? StreamingStateChanged;

        public Task<IReadOnlyList<StreamingDestinationResult>> StartStreamingAsync(
            StreamingRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult<IReadOnlyList<StreamingDestinationResult>>(
                [.. request.Destinations.Select(destination => new StreamingDestinationResult(
                    destination.ProviderId, StartResults.GetValueOrDefault(destination.ProviderId, StartResult)))]);
        }

        public Task<StudioRuntimeResult> StopStreamingAsync(CancellationToken cancellationToken) =>
            Task.FromResult(StopResult);

        public List<string> StoppedProviders { get; } = [];

        public Task<StudioRuntimeResult> StopStreamingAsync(string providerId, CancellationToken cancellationToken)
        {
            StoppedProviders.Add(providerId);
            return Task.FromResult(StopResult);
        }

        public StudioRuntimeResult SwitchStreamingScene(Guid sceneId)
        {
            SwitchedScenes.Add(sceneId);
            return SwitchResult;
        }

        public void RaiseState(bool isStreaming, string message = "", string? providerId = null) =>
            StreamingStateChanged?.Invoke(this, new StreamingStateChangedEventArgs(
                providerId ?? Request?.Destinations[0].ProviderId ?? "twitch", isStreaming, message));
    }

    private sealed class FakeRecordingRuntime : IRecordingRuntime
    {
        public bool IsAvailable => true;
        public string UnavailableMessage => "";
        public RecordingRequest? Request { get; private set; }
        public event EventHandler<RecordingStateChangedEventArgs>? StateChanged;

        public void RaiseState(RecordingState state, string message = "") =>
            StateChanged?.Invoke(this, new RecordingStateChangedEventArgs(Request?.SessionId ?? Guid.Empty, state, message));

        public Task<StudioRuntimeResult> StartRecordingAsync(
            RecordingRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(StudioRuntimeResult.Success());
        }

        public Task<StudioRuntimeResult> StopRecordingAsync(Guid sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(StudioRuntimeResult.Success());

        public Task<StudioRuntimeResult> StopRecordingAsync(CancellationToken cancellationToken) =>
            Task.FromResult(StudioRuntimeResult.Success());

        public StudioRuntimeResult SwitchRecordingScene(Guid sceneId) =>
            StudioRuntimeResult.Success();

        public string GetPauseUnavailableReason(Guid sessionId) => "";
        public StudioRuntimeResult PauseRecording(Guid sessionId) => StudioRuntimeResult.Success();
        public StudioRuntimeResult ResumeRecording(Guid sessionId) => StudioRuntimeResult.Success();
    }

    private sealed class FakeProviderStore(IEnumerable<ProviderSettings> providers) : IProviderStore
    {
        private readonly List<ProviderSettings> _providers = [.. providers];
        public event EventHandler? Changed;
        public IReadOnlyCollection<ProviderSettings> GetAll() => _providers;
        public ProviderSettings? Get(string providerId) =>
            _providers.FirstOrDefault(provider => provider.ProviderId == providerId);
        public void Save(ProviderSettings next)
        {
            _providers.RemoveAll(provider => provider.ProviderId == next.ProviderId);
            _providers.Add(next);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        public void Delete(string providerId)
        {
            _providers.RemoveAll(provider => provider.ProviderId == providerId);
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
