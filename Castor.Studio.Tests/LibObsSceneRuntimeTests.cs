using CastorApplication.Models.Settings;
using CastorApplication.Models.Studio;
using CastorApplication.Services.Settings;
using CastorApplication.Services.Studio;
using LibObs;
using System.Runtime.InteropServices;

namespace Castor.Studio.Tests;

[Collection(LibObsCollection.Name)]
public sealed class LibObsSceneRuntimeTests
{
    private readonly LibObsSceneRuntime _runtime;
    private readonly SettingsService _settings;
    private readonly string _mediaPath;
    private readonly string _videoPath;

    public LibObsSceneRuntimeTests(LibObsRuntimeFixture fixture)
    {
        _runtime = fixture.Runtime;
        _settings = fixture.Settings;
        _mediaPath = fixture.MediaPath;
        _videoPath = fixture.VideoPath;
    }

    [Fact]
    public void Streaming_request_requires_a_twitch_key()
    {
        var request = new StreamingRequest(
            new SceneDefinition(),
            [new StreamingDestination("twitch", "", "")],
            30,
            6_000,
            192,
            48_000,
            2);

        Assert.Contains("clé de stream Twitch", LibObsSceneRuntime.ValidateStreamingRequest(request));
    }

    [Theory]
    [InlineData("", "live-key", "serveur YouTube")]
    [InlineData("rtmp://a.rtmp.youtube.com", "live-key", "serveur YouTube")]
    [InlineData("https://a.rtmp.youtube.com/live2", "live-key", "serveur YouTube")]
    [InlineData("rtmp://a.rtmp.youtube.com/live2", "", "clé de stream YouTube")]
    public void YouTube_request_requires_an_ingest_server_and_a_key(string server, string key, string expected)
    {
        var request = YouTubeRequest(server, key);

        Assert.Contains(expected, LibObsSceneRuntime.ValidateStreamingRequest(request));
    }

    [Fact]
    public void YouTube_request_with_its_ingest_server_passes_validation()
    {
        Assert.Empty(LibObsSceneRuntime.ValidateStreamingRequest(
            YouTubeRequest("rtmp://a.rtmp.youtube.com/live2", "live-key")));
    }

    [Theory]
    [InlineData(ObsOutputStopCode.ConnectFailed, "Connexion au serveur YouTube impossible.")]
    [InlineData(ObsOutputStopCode.InvalidStream, "YouTube a refusé la clé de stream.")]
    [InlineData(ObsOutputStopCode.Disconnected, "La connexion au serveur YouTube a été interrompue.")]
    public void Live_stop_messages_name_the_platform(ObsOutputStopCode code, string expected)
    {
        Assert.Equal(expected, LibObsSceneRuntime.StreamingStopMessage(
            new ObsOutputStateChangedEventArgs(ObsOutputState.Stopped, code), "YouTube"));
    }

    [Fact]
    public void Streaming_destination_never_prints_its_key()
    {
        var destination = new StreamingDestination("youtube", "rtmp://a.rtmp.youtube.com/live2", "secret-key");

        Assert.DoesNotContain("secret-key", destination.ToString());
        Assert.DoesNotContain("secret-key", YouTubeRequest(destination.Server, destination.StreamKey).ToString());
    }

    private static StreamingRequest YouTubeRequest(string server, string key) => new(
        new SceneDefinition(),
        [new StreamingDestination("youtube", server, key)],
        30,
        6_000,
        192,
        48_000,
        2);

    [Fact]
    public void Streaming_request_needs_at_least_one_destination()
    {
        Assert.Contains("au moins une plateforme",
            LibObsSceneRuntime.ValidateStreamingRequest(YouTubeRequest("", "") with { Destinations = [] }));
    }

    [Fact]
    public void Streaming_request_refuses_two_lives_on_the_same_platform()
    {
        var request = YouTubeRequest("rtmp://a.rtmp.youtube.com/live2", "first") with
        {
            Destinations =
            [
                new StreamingDestination("youtube", "rtmp://a.rtmp.youtube.com/live2", "first"),
                new StreamingDestination("youtube", "rtmp://b.rtmp.youtube.com/live2", "second")
            ]
        };

        Assert.Contains("un seul live", LibObsSceneRuntime.ValidateStreamingRequest(request));
    }

    [Fact]
    public void Streaming_request_checks_every_destination()
    {
        var request = YouTubeRequest("rtmp://a.rtmp.youtube.com/live2", "yt-key") with
        {
            Destinations =
            [
                new StreamingDestination("youtube", "rtmp://a.rtmp.youtube.com/live2", "yt-key"),
                new StreamingDestination("twitch", "", "")
            ]
        };

        Assert.Contains("clé de stream Twitch", LibObsSceneRuntime.ValidateStreamingRequest(request));
        Assert.Empty(LibObsSceneRuntime.ValidateStreamingRequest(request with
        {
            Destinations = [request.Destinations[0], new StreamingDestination("twitch", "", "tw-key")]
        }));
    }

    [Fact]
    public void Live_refused_at_start_without_reason_names_its_platform()
    {
        Assert.Equal("Le live YouTube s'est arrêté dès son démarrage.", LibObsSceneRuntime.FailedStreamingStartMessage(
            new ObsOutputStateChangedEventArgs(ObsOutputState.Stopped, ObsOutputStopCode.Success), "YouTube"));
        Assert.Equal("Twitch a refusé la clé de stream.", LibObsSceneRuntime.FailedStreamingStartMessage(
            new ObsOutputStateChangedEventArgs(ObsOutputState.Stopped, ObsOutputStopCode.InvalidStream), "Twitch"));
    }

    // Nothing listens on port 1: both lives fail to connect. What the native runtime proves
    // here is that each platform gets its own outcome, and that the shared encoders and the
    // outputs are all released, so the next start finds a free runtime.
    [Fact]
    public async Task Native_runtime_reports_each_live_of_a_multistream_on_its_own()
    {
        var runtime = _runtime;
        var scene = new SceneDefinition { Name = "Multistream" };
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            Assert.True(runtime.CreateScene(scene.Id, scene.Name).IsSuccess);
            Assert.True(runtime.AddSource(scene.Id,
                new SourceAddRequest.Media(Guid.NewGuid(), "Live source", _mediaPath, true)).IsSuccess);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var request = new StreamingRequest(
                scene,
                [
                    new StreamingDestination("youtube", "rtmp://127.0.0.1:1/live2", "first-key"),
                    new StreamingDestination("other", "rtmp://127.0.0.1:1/app", "second-key")
                ],
                30,
                1_000,
                128,
                48_000,
                2);

            for (var cycle = 0; cycle < 2; cycle++)
            {
                var results = await runtime.StartStreamingAsync(request, timeout.Token);

                Assert.Equal(["youtube", "other"], results.Select(result => result.ProviderId));
                Assert.All(results, result =>
                {
                    Assert.False(result.Result.IsSuccess);
                    Assert.NotEmpty(result.Result.Message);
                });
                // Settles the outputs that ended on their own without waiting for their release.
                Assert.True((await runtime.StopStreamingAsync(timeout.Token)).IsSuccess);
            }

            Assert.True(runtime.RemoveScene(scene.Id).IsSuccess);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await runtime.StopStreamingAsync(cleanup.Token);
            RemoveScenes(scene.Id);
        }
    }

    [Fact]
    public async Task Native_runtime_refuses_a_live_on_an_unknown_scene_for_every_destination()
    {
        var runtime = _runtime;
        Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
        var request = YouTubeRequest("rtmp://127.0.0.1:1/live2", "key") with
        {
            Scene = new SceneDefinition(),
            Destinations =
            [
                new StreamingDestination("youtube", "rtmp://127.0.0.1:1/live2", "key"),
                new StreamingDestination("twitch", "", "key")
            ]
        };

        var results = await runtime.StartStreamingAsync(request, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.Contains("n'existe pas", result.Result.Message));
    }

    // libobs can end an output right after its start with a success code and no error: the
    // user still has to be told, or the RECORD button just flickers back.
    [Theory]
    [InlineData(ObsOutputStopCode.Success, null)]
    [InlineData(ObsOutputStopCode.Success, "")]
    [InlineData(null, null)]
    public void Unexpected_recording_stop_without_reason_still_has_a_message(ObsOutputStopCode? code, string? error)
    {
        var message = LibObsSceneRuntime.UnexpectedRecordingStopMessage(
            new ObsOutputStateChangedEventArgs(ObsOutputState.Stopped, code, error));

        Assert.Contains("arrêté de lui-même", message);
    }

    [Fact]
    public void Unexpected_recording_stop_keeps_the_libobs_reason()
    {
        Assert.Equal("disque plein", LibObsSceneRuntime.UnexpectedRecordingStopMessage(
            new ObsOutputStateChangedEventArgs(ObsOutputState.Stopped, ObsOutputStopCode.Success, "disque plein")));
        Assert.Contains("Espace disque insuffisant", LibObsSceneRuntime.UnexpectedRecordingStopMessage(
            new ObsOutputStateChangedEventArgs(ObsOutputState.Stopped, ObsOutputStopCode.NoSpace)));
    }

    [Theory]
    [InlineData(0, 6_000, 192, 48_000, 2)]
    [InlineData(30, 0, 192, 48_000, 2)]
    [InlineData(30, 6_000, 0, 48_000, 2)]
    [InlineData(30, 6_000, 192, 0, 2)]
    [InlineData(30, 6_000, 192, 48_000, 3)]
    public void Streaming_request_rejects_invalid_media_settings(
        int fps,
        int videoBitrate,
        int audioBitrate,
        int sampleRate,
        int channels)
    {
        var request = new StreamingRequest(
            new SceneDefinition(),
            [new StreamingDestination("twitch", "", "live-key")],
            fps,
            videoBitrate,
            audioBitrate,
            sampleRate,
            channels);

        Assert.NotEmpty(LibObsSceneRuntime.ValidateStreamingRequest(request));
    }

    [Fact]
    public void Valid_streaming_request_passes_validation()
    {
        var request = new StreamingRequest(
            new SceneDefinition(),
            [new StreamingDestination("twitch", "", "live-key")],
            30,
            6_000,
            192,
            48_000,
            2);

        Assert.Empty(LibObsSceneRuntime.ValidateStreamingRequest(request));
    }

    [Fact]
    public void Rtmp_camera_opens_its_stream_without_waiting_for_the_program_output()
    {
        var settings = LibObsSceneRuntime.CreateNetworkMediaSourceSettings(
            new SourceAddRequest.Network(Guid.NewGuid(), "Caméra", "rtmp://192.168.1.20/live/camera"));

        Assert.Equal("rtmp://192.168.1.20/live/camera", settings.Url);
        Assert.False(settings.RestartWhenActivated);
        Assert.False(settings.CloseWhenInactive);
    }

    [Fact]
    public void Native_runtime_adds_an_unreachable_rtmp_camera_and_refuses_another_scheme()
    {
        var runtime = _runtime;
        var sceneId = Guid.NewGuid();
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            Assert.True(runtime.CreateScene(sceneId, "RTMP test").IsSuccess);

            var refused = runtime.AddSource(sceneId,
                new SourceAddRequest.Network(Guid.NewGuid(), "RTSP", "rtsp://127.0.0.1/live/secret-key"));
            Assert.False(refused.IsSuccess);
            Assert.DoesNotContain("secret-key", refused.Message);

            // Nothing listens on port 1: the camera is added anyway and keeps reconnecting.
            var sourceId = Guid.NewGuid();
            var added = runtime.AddSource(sceneId,
                new SourceAddRequest.Network(sourceId, "Caméra", "rtmp://127.0.0.1:1/live/test"));
            Assert.True(added.IsSuccess, added.Message);
            Assert.False(runtime.SetMediaLoop(sceneId, sourceId, false).IsSuccess);
            Assert.True(runtime.RemoveSource(sceneId, sourceId).IsSuccess);
        }
        finally
        {
            RemoveScenes(sceneId);
        }
    }

    [Fact]
    public void Windows_capture_uses_safe_fallbacks_when_libobs_winrt_is_missing()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), $"castor-winrt-{Guid.NewGuid():N}");
        var runtimeDirectory = Path.Combine(baseDirectory, "obs-runtime", "bin", "64bit");
        Directory.CreateDirectory(runtimeDirectory);

        try
        {
            Assert.False(LibObsSceneRuntime.HasWinRtCaptureRuntime(baseDirectory));

            var displaySettings = LibObsSceneRuntime.CreateDisplayCaptureSettings("monitor", baseDirectory);
            var windowSettings = LibObsSceneRuntime.CreateWindowCaptureSettings("window", baseDirectory);

            Assert.Equal(ObsWindowsDisplayCaptureMethod.DxgiDesktopDuplication, displaySettings.Method);
            Assert.Equal(ObsWindowsWindowCaptureMethod.BitBlt, windowSettings.Method);

            File.WriteAllBytes(Path.Combine(runtimeDirectory, "libobs-winrt.dll"), []);

            Assert.True(LibObsSceneRuntime.HasWinRtCaptureRuntime(baseDirectory));
            Assert.Equal(
                ObsWindowsDisplayCaptureMethod.Automatic,
                LibObsSceneRuntime.CreateDisplayCaptureSettings("monitor", baseDirectory).Method);
            Assert.Equal(
                ObsWindowsWindowCaptureMethod.Automatic,
                LibObsSceneRuntime.CreateWindowCaptureSettings("window", baseDirectory).Method);
        }
        finally
        {
            if (Directory.Exists(baseDirectory)) Directory.Delete(baseDirectory, recursive: true);
        }
    }

    [Fact]
    public void Shipped_runtime_includes_libobs_winrt_for_windows_graphics_capture()
    {
        // Without this DLL, capture silently falls back to DXGI/BitBlt: the
        // LibObs.Runtime package must ship it and list it in its manifest.
        var baseDirectory = AppContext.BaseDirectory;
        Assert.True(LibObsSceneRuntime.HasWinRtCaptureRuntime(baseDirectory));

        var manifest = File.ReadAllText(Path.Combine(baseDirectory, "obs-runtime", "runtime-manifest.json"));
        Assert.Contains("\"bin/64bit/libobs-winrt.dll\"", manifest);
    }

    [Fact]
    public void Recording_video_settings_keep_base_canvas_and_requested_output_resolution()
    {
        var request = new RecordingRequest(
            Guid.NewGuid(),
            "recording.mkv",
            Fps: 60,
            VideoBitrateKbps: 8_000,
            AudioBitrateKbps: 192,
            AudioSampleRate: 48_000,
            AudioChannels: 2,
            BaseWidth: 2560,
            BaseHeight: 1440,
            OutputWidth: 2560,
            OutputHeight: 1440,
            RecordingContainer.Mkv);

        var settings = LibObsSceneRuntime.CreateRecordingVideoSettings(request);

        Assert.Equal(2560u, settings.BaseWidth);
        Assert.Equal(1440u, settings.BaseHeight);
        Assert.Equal(2560u, settings.OutputWidth);
        Assert.Equal(1440u, settings.OutputHeight);
    }

    [Fact]
    public void Preview_video_settings_forward_the_resolved_canvas_to_libobs()
    {
        var settings = LibObsSceneRuntime.CreatePreviewVideoSettings(
            new ApplicationSettings(),
            new VideoCanvasResolution(3440, 1440));

        Assert.Equal(3440u, settings.BaseWidth);
        Assert.Equal(1440u, settings.BaseHeight);
    }

    [Fact]
    public void Native_scene_lifecycle_deduplicates_names()
    {
        var runtime = _runtime;
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            var requestedName = $"Castor test {Guid.NewGuid():N}";

            var first = runtime.CreateScene(firstId, requestedName);
            var second = runtime.CreateScene(secondId, requestedName);

            Assert.True(first.IsSuccess, first.Message);
            Assert.True(second.IsSuccess, second.Message);
            Assert.Equal(requestedName, first.EffectiveName);
            Assert.Equal($"{requestedName} 2", second.EffectiveName);

            var renamed = runtime.RenameScene(secondId, requestedName);
            Assert.True(renamed.IsSuccess, renamed.Message);
            Assert.Equal($"{requestedName} 2", renamed.EffectiveName);

            Assert.True(runtime.RemoveScene(firstId).IsSuccess);
            Assert.True(runtime.RemoveScene(secondId).IsSuccess);
        }
        finally
        {
            RemoveScenes(firstId, secondId);
        }
    }

    [Fact]
    public async Task Native_runtime_enumerates_and_manages_a_media_source()
    {
        var mediaPath = _mediaPath;
        var runtime = _runtime;
        var sceneId = Guid.NewGuid();
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            var catalog = await runtime.EnumerateSourcesAsync(CancellationToken.None);
            Assert.NotNull(catalog.VideoSources);
            Assert.NotNull(catalog.AudioSources);
            Assert.True(string.IsNullOrEmpty(catalog.Message), catalog.Message);

            var sourceId = Guid.NewGuid();
            Assert.True(runtime.CreateScene(sceneId, "Media test").IsSuccess);

            var missing = runtime.AddSource(sceneId,
                new SourceAddRequest.Media(sourceId, "Absent", mediaPath + ".missing", true));
            Assert.False(missing.IsSuccess);
            Assert.Contains("does not exist", missing.Message);

            var added = runtime.AddSource(sceneId,
                new SourceAddRequest.Media(sourceId, "Silence", mediaPath, true));
            Assert.True(added.IsSuccess, added.Message);
            Assert.True(runtime.SetMediaLoop(sceneId, sourceId, false).IsSuccess);
            Assert.True(runtime.RemoveSource(sceneId, sourceId).IsSuccess);
            Assert.True(runtime.RemoveScene(sceneId).IsSuccess);
        }
        finally
        {
            RemoveScenes(sceneId);
        }
    }

    [Fact]
    public void Native_runtime_writes_a_transform_and_keeps_it_whole_when_refused()
    {
        var mediaPath = _mediaPath;
        var runtime = _runtime;
        var sceneId = Guid.NewGuid();
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            var sourceId = Guid.NewGuid();
            Assert.True(runtime.CreateScene(sceneId, "Transform test").IsSuccess);
            Assert.True(runtime.AddSource(sceneId,
                new SourceAddRequest.Media(sourceId, "Silence", mediaPath, true)).IsSuccess);

            var written = runtime.SetSourceTransform(sceneId, sourceId,
                new SourcePlacement(320, 180, 0.5, 0.75, SourceCrop.None));
            Assert.True(written.IsSuccess, written.Message);
            Assert.Equal(new SourcePlacement(320, 180, 0.5, 0.75, SourceCrop.None), written.Transform!.Placement);

            // What is written is what the engine returns on the next read.
            var read = runtime.GetSceneComposition(sceneId);
            Assert.True(read.IsSuccess, read.Message);
            Assert.Equal(written.Transform.Placement, Assert.Single(read.Composition.Sources).Placement);

            // A refused placement leaves nothing behind.
            Assert.False(runtime.SetSourceTransform(sceneId, sourceId,
                new SourcePlacement(0, 0, 0, 1, SourceCrop.None)).IsSuccess);
            Assert.False(runtime.SetSourceTransform(sceneId, sourceId,
                new SourcePlacement(double.NaN, 0, 1, 1, SourceCrop.None)).IsSuccess);
            Assert.False(runtime.SetSourceTransform(sceneId, sourceId,
                new SourcePlacement(0, 0, 1, 1, new SourceCrop(-1, 0, 0, 0))).IsSuccess);
            Assert.False(runtime.SetSourceTransform(sceneId, Guid.NewGuid(),
                new SourcePlacement(0, 0, 1, 1, SourceCrop.None)).IsSuccess);

            // Rotation goes with position and scale, and reads back as written.
            var rotated = runtime.SetSourceTransform(sceneId, sourceId,
                written.Transform.Placement with { Rotation = 30 });
            Assert.True(rotated.IsSuccess, rotated.Message);
            Assert.Equal(30, rotated.Transform!.Rotation, 3);
            Assert.Equal(30, Assert.Single(runtime.GetSceneComposition(sceneId).Composition.Sources).Rotation, 3);

            // A non-finite rotation is refused without touching the source.
            Assert.False(runtime.SetSourceTransform(sceneId, sourceId,
                written.Transform.Placement with { Rotation = double.NaN }).IsSuccess);
            Assert.Equal(30, Assert.Single(runtime.GetSceneComposition(sceneId).Composition.Sources).Rotation, 3);
            Assert.True(runtime.SetSourceTransform(sceneId, sourceId, written.Transform.Placement).IsSuccess);

            var after = runtime.GetSceneComposition(sceneId);
            Assert.Equal(written.Transform.Placement, Assert.Single(after.Composition.Sources).Placement);

            // A sound has no image to enlarge; the full image can always be requested again.
            Assert.False(runtime.SetSourceZoom(sceneId, sourceId, new SourceZoom(2)).IsSuccess);
            Assert.False(runtime.SetSourceZoom(sceneId, sourceId, new SourceZoom(double.NaN)).IsSuccess);
            Assert.False(runtime.SetSourceZoom(sceneId, sourceId, new SourceZoom(1, 2, 0.5)).IsSuccess);
            var unzoomed = runtime.SetSourceZoom(sceneId, sourceId, SourceZoom.None);
            Assert.True(unzoomed.IsSuccess, unzoomed.Message);
            Assert.Equal(written.Transform.Placement, unzoomed.Transform!.Placement);

            Assert.True(runtime.RemoveSource(sceneId, sourceId).IsSuccess);
            Assert.True(runtime.RemoveScene(sceneId).IsSuccess);
        }
        finally
        {
            RemoveScenes(sceneId);
        }
    }

    [Fact]
    public async Task Native_runtime_adds_and_removes_a_monitor_source()
    {
        if (!OperatingSystem.IsWindows()) return;

        var runtime = _runtime;
        Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);

        var catalog = await runtime.EnumerateSourcesAsync(CancellationToken.None);
        var monitor = catalog.VideoSources.FirstOrDefault(source => source.Type == VideoCaptureKind.Monitor);
        if (monitor == null) return;

        var sceneId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        Assert.True(runtime.CreateScene(sceneId, "Monitor capture test").IsSuccess);

        try
        {
            var added = runtime.AddSource(sceneId,
                new SourceAddRequest.Video(sourceId, "Monitor capture", monitor));
            Assert.True(added.IsSuccess, added.Message);
            await Task.Delay(250);
            Assert.True(runtime.RemoveSource(sceneId, sourceId).IsSuccess);
        }
        finally
        {
            runtime.RemoveScene(sceneId);
        }
    }

    [Fact]
    public async Task Native_runtime_adds_and_removes_a_window_source()
    {
        if (!OperatingSystem.IsWindows()) return;

        var runtime = _runtime;
        Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);

        var catalog = await runtime.EnumerateSourcesAsync(CancellationToken.None);
        var window = catalog.VideoSources.FirstOrDefault(source => source.Type == VideoCaptureKind.Window);
        if (window == null) return;

        var sceneId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        Assert.True(runtime.CreateScene(sceneId, "Window capture test").IsSuccess);

        try
        {
            var added = runtime.AddSource(sceneId,
                new SourceAddRequest.Video(sourceId, "Window capture", window));
            Assert.True(added.IsSuccess, added.Message);
            await Task.Delay(250);
            Assert.True(runtime.RemoveSource(sceneId, sourceId).IsSuccess);
        }
        finally
        {
            runtime.RemoveScene(sceneId);
        }
    }

    [Theory]
    [InlineData(RecordingContainer.Mp4, ".mp4")]
    [InlineData(RecordingContainer.Mkv, ".mkv")]
    [InlineData(RecordingContainer.WebM, ".webm")]
    public async Task Native_runtime_records_the_created_scene(
        RecordingContainer container,
        string extension)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"castor-record-{Guid.NewGuid():N}{extension}");
        var mediaPath = _mediaPath;
        var runtime = _runtime;
        var sceneId = Guid.NewGuid();
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            var sourceId = Guid.NewGuid();
            Assert.True(runtime.CreateScene(sceneId, $"Record {container}").IsSuccess);
            Assert.True(runtime.AddSource(sceneId,
                new SourceAddRequest.Media(sourceId, "Recording source", mediaPath, true)).IsSuccess);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            var started = await runtime.StartRecordingAsync(CreateRecordingRequest(sceneId, outputPath, container), timeout.Token);
            Assert.True(started.IsSuccess, started.Message);
            Assert.Contains("utilisée", runtime.RemoveScene(sceneId).Message);

            await Task.Delay(2_000, timeout.Token);
            var stopped = await runtime.StopRecordingAsync(timeout.Token);

            Assert.True(stopped.IsSuccess, stopped.Message);
            Assert.True(File.Exists(outputPath));
            Assert.True(new FileInfo(outputPath).Length > 0);
            Assert.True(runtime.RemoveScene(sceneId).IsSuccess);
        }
        finally
        {
            await StopRecordingAndRemoveScenesAsync(sceneId);
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task Native_runtime_supports_two_recordings_in_sequence()
    {
        var firstPath = Path.Combine(Path.GetTempPath(), $"castor-record-{Guid.NewGuid():N}.mkv");
        var secondPath = Path.Combine(Path.GetTempPath(), $"castor-record-{Guid.NewGuid():N}.mkv");
        var mediaPath = _mediaPath;
        var runtime = _runtime;
        var sceneId = Guid.NewGuid();
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            Assert.True(runtime.CreateScene(sceneId, "Record twice").IsSuccess);
            Assert.True(runtime.AddSource(sceneId,
                new SourceAddRequest.Media(Guid.NewGuid(), "Recording source", mediaPath, true)).IsSuccess);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            Assert.True((await runtime.StartRecordingAsync(
                CreateRecordingRequest(sceneId, firstPath, RecordingContainer.Mkv), timeout.Token)).IsSuccess);
            Assert.False((await runtime.StartRecordingAsync(
                CreateRecordingRequest(sceneId, secondPath, RecordingContainer.Mkv), timeout.Token)).IsSuccess);
            await Task.Delay(300, timeout.Token);
            Assert.True((await runtime.StopRecordingAsync(timeout.Token)).IsSuccess);

            Assert.True((await runtime.StartRecordingAsync(
                CreateRecordingRequest(sceneId, secondPath, RecordingContainer.Mkv), timeout.Token)).IsSuccess);
            await Task.Delay(300, timeout.Token);
            Assert.True((await runtime.StopRecordingAsync(timeout.Token)).IsSuccess);
        }
        finally
        {
            await StopRecordingAndRemoveScenesAsync(sceneId);
            File.Delete(firstPath);
            File.Delete(secondPath);
        }
    }

    [Fact]
    public async Task Native_runtime_rejects_an_unknown_or_empty_scene()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"castor-record-{Guid.NewGuid():N}.mp4");
        var runtime = _runtime;
        var emptySceneId = Guid.NewGuid();
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            var unknown = await runtime.StartRecordingAsync(
                CreateRecordingRequest(Guid.NewGuid(), outputPath, RecordingContainer.Mp4), timeout.Token);
            Assert.False(unknown.IsSuccess);
            Assert.Contains("n'existe pas", unknown.Message);

            Assert.True(runtime.CreateScene(emptySceneId, "Empty record").IsSuccess);
            var empty = await runtime.StartRecordingAsync(
                CreateRecordingRequest(emptySceneId, outputPath, RecordingContainer.Mp4), timeout.Token);
            Assert.False(empty.IsSuccess);
            Assert.Contains("source vidéo ou média", empty.Message);
        }
        finally
        {
            await StopRecordingAndRemoveScenesAsync(emptySceneId);
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task Native_preview_rejects_an_invalid_surface_and_unknown_scene()
    {
        var runtime = _runtime;
        Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var invalidSurface = await runtime.StartPreviewAsync(
            new SceneDefinition(), IntPtr.Zero, 320, 180, timeout.Token);
        Assert.False(invalidSurface.IsSuccess);
        Assert.Contains("handle", invalidSurface.Message);

        var unknown = await runtime.StartPreviewAsync(
            new SceneDefinition(), new IntPtr(1), 320, 180, timeout.Token);
        Assert.False(unknown.IsSuccess);
        Assert.Contains("n'existe pas", unknown.Message);
    }

    [Fact]
    public async Task Native_preview_decodes_the_frames_of_a_media_source()
    {
        if (!OperatingSystem.IsWindows()) return;

        var windowHandle = CreateWindowEx(
            0, "STATIC", "Castor media preview test", WindowStylePopup,
            0, 0, 320, 180, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, windowHandle);

        var mediaPath = _videoPath;
        var runtime = _runtime;
        var scene = new SceneDefinition { Name = "Media preview" };
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            var sourceId = Guid.NewGuid();
            Assert.True(runtime.CreateScene(scene.Id, scene.Name).IsSuccess);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Assert.True((await runtime.StartPreviewAsync(scene, windowHandle, 320, 180, timeout.Token)).IsSuccess);
            Assert.True(runtime.AddSource(scene.Id,
                new SourceAddRequest.Media(sourceId, "Clip", mediaPath, true)).IsSuccess);

            // A media source has no size until its first frame is decoded.
            SourceTransform? decoded = null;
            while (!timeout.IsCancellationRequested)
            {
                decoded = Assert.Single(runtime.GetSceneComposition(scene.Id).Composition.Sources);
                if (decoded.SourceWidth > 0) break;
                await Task.Delay(50);
            }

            Assert.Equal(64, decoded!.SourceWidth);
            Assert.Equal(36, decoded.SourceHeight);
            Assert.True((await runtime.StopPreviewAsync(windowHandle, scene.Id, CancellationToken.None)).IsSuccess);
        }
        finally
        {
            // Removing the scene also closes a preview left open by a failed assertion,
            // before its window goes.
            RemoveScenes(scene.Id);
            DestroyWindow(windowHandle);
        }
    }

    [Fact]
    public async Task Native_preview_keeps_an_empty_scene_alive_while_sources_change()
    {
        if (!OperatingSystem.IsWindows()) return;

        var windowHandle = CreateWindowEx(
            0, "STATIC", "Castor preview test", WindowStylePopup,
            0, 0, 320, 180, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, windowHandle);

        var mediaPath = _mediaPath;
        var runtime = _runtime;
        var scene = new SceneDefinition { Name = "Empty preview" };
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            var sourceId = Guid.NewGuid();
            Assert.True(runtime.CreateScene(scene.Id, scene.Name).IsSuccess);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var started = await runtime.StartPreviewAsync(
                scene, windowHandle, 320, 180, timeout.Token);

            Assert.True(started.IsSuccess, started.Message);
            Assert.True(runtime.AddSource(scene.Id,
                new SourceAddRequest.Media(sourceId, "Preview source", mediaPath, true)).IsSuccess);
            Assert.True(runtime.RemoveSource(scene.Id, sourceId).IsSuccess);
            Assert.True((await runtime.StopPreviewAsync(windowHandle, scene.Id, timeout.Token)).IsSuccess);
            Assert.True(runtime.RemoveScene(scene.Id).IsSuccess);
        }
        finally
        {
            RemoveScenes(scene.Id);
            DestroyWindow(windowHandle);
        }
    }

    [Fact]
    public async Task Two_windows_can_preview_the_same_scene_at_once()
    {
        if (!OperatingSystem.IsWindows()) return;

        var firstWindow = CreateWindowEx(
            0, "STATIC", "Castor preview test 1", WindowStylePopup,
            0, 0, 320, 180, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        var secondWindow = CreateWindowEx(
            0, "STATIC", "Castor preview test 2", WindowStylePopup,
            0, 0, 320, 180, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, firstWindow);
        Assert.NotEqual(IntPtr.Zero, secondWindow);

        var runtime = _runtime;
        var scene = new SceneDefinition { Name = "Shared preview" };
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            Assert.True(runtime.CreateScene(scene.Id, scene.Name).IsSuccess);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Studio panel and Scenes page, both showing the active scene at once: neither
            // start should tear down the other's session.
            var first = await runtime.StartPreviewAsync(scene, firstWindow, 320, 180, timeout.Token);
            var second = await runtime.StartPreviewAsync(scene, secondWindow, 320, 180, timeout.Token);
            Assert.True(first.IsSuccess, first.Message);
            Assert.True(second.IsSuccess, second.Message);

            // The first window's session is still live: resizing and stopping it must
            // still work, exactly as if the second window had never opened.
            runtime.ResizePreview(firstWindow, 400, 300);
            Assert.True((await runtime.StopPreviewAsync(firstWindow, scene.Id, timeout.Token)).IsSuccess);

            // Stopping the first window's (already-stopped) session must not touch the
            // second window's, still showing the same scene.
            Assert.True((await runtime.StopPreviewAsync(secondWindow, scene.Id, timeout.Token)).IsSuccess);
            Assert.True(runtime.RemoveScene(scene.Id).IsSuccess);
        }
        finally
        {
            RemoveScenes(scene.Id);
            DestroyWindow(firstWindow);
            DestroyWindow(secondWindow);
        }
    }

    [Fact]
    public async Task Removing_a_scene_stops_every_window_previewing_it()
    {
        if (!OperatingSystem.IsWindows()) return;

        var firstWindow = CreateWindowEx(
            0, "STATIC", "Castor preview test 1", WindowStylePopup,
            0, 0, 320, 180, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        var secondWindow = CreateWindowEx(
            0, "STATIC", "Castor preview test 2", WindowStylePopup,
            0, 0, 320, 180, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        var runtime = _runtime;
        var scene = new SceneDefinition { Name = "Removed while shown twice" };
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            Assert.True(runtime.CreateScene(scene.Id, scene.Name).IsSuccess);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            Assert.True((await runtime.StartPreviewAsync(scene, firstWindow, 320, 180, timeout.Token)).IsSuccess);
            Assert.True((await runtime.StartPreviewAsync(scene, secondWindow, 320, 180, timeout.Token)).IsSuccess);

            // Removing the scene must not leave either window's session dangling on a
            // now-destroyed native source.
            Assert.True(runtime.RemoveScene(scene.Id).IsSuccess);
        }
        finally
        {
            RemoveScenes(scene.Id);
            DestroyWindow(firstWindow);
            DestroyWindow(secondWindow);
        }
    }

    [Fact]
    public async Task Switching_scene_mid_recording_re_points_the_live_output()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"castor-switch-{Guid.NewGuid():N}.mkv");
        var mediaPath = _mediaPath;
        var runtime = _runtime;
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            Assert.True(runtime.CreateScene(first, "Plateau").IsSuccess);
            Assert.True(runtime.CreateScene(second, "Caméra").IsSuccess);
            Assert.True(runtime.AddSource(first,
                new SourceAddRequest.Media(Guid.NewGuid(), "Source 1", mediaPath, true)).IsSuccess);
            Assert.True(runtime.AddSource(second,
                new SourceAddRequest.Media(Guid.NewGuid(), "Source 2", mediaPath, true)).IsSuccess);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

            Assert.True((await runtime.StartRecordingAsync(
                CreateRecordingRequest(first, outputPath, RecordingContainer.Mkv), timeout.Token)).IsSuccess);
            await Task.Delay(500, timeout.Token);

            // The real native call, on a running output - what a fake runtime cannot prove.
            var switched = runtime.SwitchRecordingScene(second);
            Assert.True(switched.IsSuccess, switched.Message);
            await Task.Delay(500, timeout.Token);

            // The guard follows the scene actually being recorded now.
            Assert.True(runtime.RemoveScene(first).IsSuccess);
            Assert.Contains("utilisée", runtime.RemoveScene(second).Message);

            var stopped = await runtime.StopRecordingAsync(timeout.Token);
            Assert.True(stopped.IsSuccess, stopped.Message);
            Assert.True(File.Exists(outputPath));
            Assert.True(new FileInfo(outputPath).Length > 0);
        }
        finally
        {
            await StopRecordingAndRemoveScenesAsync(first, second);
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task Live_started_and_lost_mid_recording_leaves_the_recording_running()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"castor-record-live-{Guid.NewGuid():N}.mkv");
        var runtime = _runtime;
        var first = new SceneDefinition { Name = "Plateau" };
        var second = Guid.NewGuid();
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            Assert.True(runtime.CreateScene(first.Id, first.Name).IsSuccess);
            Assert.True(runtime.CreateScene(second, "Caméra").IsSuccess);
            Assert.True(runtime.AddSource(first.Id,
                new SourceAddRequest.Media(Guid.NewGuid(), "Source 1", _mediaPath, true)).IsSuccess);
            Assert.True(runtime.AddSource(second,
                new SourceAddRequest.Media(Guid.NewGuid(), "Source 2", _mediaPath, true)).IsSuccess);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            Assert.True((await runtime.StartRecordingAsync(
                CreateRecordingRequest(first.Id, outputPath, RecordingContainer.Mkv), timeout.Token)).IsSuccess);
            await Task.Delay(300, timeout.Token);

            // Joins the recording on the program channel, with the audio it already runs: no
            // refusal, and no audio reset under the running output. The server is unreachable,
            // so the live ends at once and lets the channel go.
            var results = await runtime.StartStreamingAsync(new StreamingRequest(
                first, [new StreamingDestination("youtube", "rtmp://127.0.0.1:1/live2", "key")],
                30, 1_000, 128, 48_000, 2), timeout.Token);
            var live = Assert.Single(results);
            Assert.False(live.Result.IsSuccess);
            Assert.DoesNotContain("Arrêtez", live.Result.Message);
            Assert.True((await runtime.StopStreamingAsync(timeout.Token)).IsSuccess);

            // The recording still owns the program channel: it switches scene and ends cleanly.
            var switched = runtime.SwitchRecordingScene(second);
            Assert.True(switched.IsSuccess, switched.Message);
            await Task.Delay(300, timeout.Token);
            var stopped = await runtime.StopRecordingAsync(timeout.Token);
            Assert.True(stopped.IsSuccess, stopped.Message);
            Assert.True(new FileInfo(outputPath).Length > 0);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await runtime.StopStreamingAsync(cleanup.Token);
            await StopRecordingAndRemoveScenesAsync(first.Id, second);
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task Live_with_other_audio_settings_than_a_running_recording_is_refused()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"castor-record-audio-{Guid.NewGuid():N}.mkv");
        var runtime = _runtime;
        var scene = new SceneDefinition { Name = "Plateau" };
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            Assert.True(runtime.CreateScene(scene.Id, scene.Name).IsSuccess);
            Assert.True(runtime.AddSource(scene.Id,
                new SourceAddRequest.Media(Guid.NewGuid(), "Source", _mediaPath, true)).IsSuccess);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            Assert.True((await runtime.StartRecordingAsync(
                CreateRecordingRequest(scene.Id, outputPath, RecordingContainer.Mkv), timeout.Token)).IsSuccess);

            // libobs cannot reset audio under the running recording: 44.1 kHz mono is refused.
            var results = await runtime.StartStreamingAsync(new StreamingRequest(
                scene, [new StreamingDestination("youtube", "rtmp://127.0.0.1:1/live2", "key")],
                30, 1_000, 128, 44_100, 1), timeout.Token);

            Assert.Contains("réglages audio", Assert.Single(results).Result.Message);
            Assert.True((await runtime.StopRecordingAsync(timeout.Token)).IsSuccess);
        }
        finally
        {
            await StopRecordingAndRemoveScenesAsync(scene.Id);
            File.Delete(outputPath);
        }
    }

    [Theory]
    [InlineData(SceneTransitionKind.Cut)]
    [InlineData(SceneTransitionKind.Fade)]
    [InlineData(SceneTransitionKind.FadeToBlack)]
    [InlineData(SceneTransitionKind.Swipe)]
    [InlineData(SceneTransitionKind.Slide)]
    public async Task Switching_scene_mid_recording_plays_each_transition(SceneTransitionKind kind)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"castor-transition-{Guid.NewGuid():N}.mkv");
        var mediaPath = _mediaPath;
        // The shared runtime reloads its transition from these settings when they are saved.
        var settingsService = _settings;
        settingsService.Save(new ApplicationSettings { SceneTransitionKind = kind, SceneTransitionDurationMs = 150 });
        var runtime = _runtime;
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            Assert.True(runtime.CreateScene(first, "Plateau").IsSuccess);
            Assert.True(runtime.CreateScene(second, "Caméra").IsSuccess);
            Assert.True(runtime.AddSource(first,
                new SourceAddRequest.Media(Guid.NewGuid(), "Source 1", mediaPath, true)).IsSuccess);
            Assert.True(runtime.AddSource(second,
                new SourceAddRequest.Media(Guid.NewGuid(), "Source 2", mediaPath, true)).IsSuccess);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

            Assert.True((await runtime.StartRecordingAsync(
                CreateRecordingRequest(first, outputPath, RecordingContainer.Mkv), timeout.Token)).IsSuccess);
            await Task.Delay(300, timeout.Token);

            var switched = runtime.SwitchRecordingScene(second);
            Assert.True(switched.IsSuccess, switched.Message);
            await Task.Delay(300, timeout.Token);

            // Changed mid-recording: the next switch rebuilds the transition on the scene shown.
            settingsService.Save(new ApplicationSettings
            {
                SceneTransitionKind = kind == SceneTransitionKind.Fade ? SceneTransitionKind.Slide : SceneTransitionKind.Fade,
                SceneTransitionDurationMs = 150
            });
            var switchedBack = runtime.SwitchRecordingScene(first);
            Assert.True(switchedBack.IsSuccess, switchedBack.Message);
            await Task.Delay(300, timeout.Token);

            var stopped = await runtime.StopRecordingAsync(timeout.Token);
            Assert.True(stopped.IsSuccess, stopped.Message);
            Assert.True(new FileInfo(outputPath).Length > 0);
        }
        finally
        {
            await StopRecordingAndRemoveScenesAsync(first, second);
            // Back to the default transition for the tests that follow.
            settingsService.Save(new ApplicationSettings());
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void Switching_scene_without_a_recording_is_a_no_op()
    {
        var runtime = _runtime;
        var sceneId = Guid.NewGuid();
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            Assert.True(runtime.CreateScene(sceneId, "Hors enregistrement").IsSuccess);

            // Nothing is running, so there is no output to re-point - and an unknown scene
            // is not reported as an error either, since nothing was asked of LibObs.
            Assert.True(runtime.SwitchRecordingScene(sceneId).IsSuccess);
            Assert.True(runtime.SwitchRecordingScene(Guid.NewGuid()).IsSuccess);
        }
        finally
        {
            RemoveScenes(sceneId);
        }
    }

    [Fact]
    public void Switching_scene_without_a_live_is_a_no_op()
    {
        var runtime = _runtime;
        var sceneId = Guid.NewGuid();
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            Assert.True(runtime.CreateScene(sceneId, "Hors live").IsSuccess);

            // Nothing is running, so there is no output to re-point - and an unknown scene
            // is not reported as an error either, since nothing was asked of LibObs.
            Assert.True(runtime.SwitchStreamingScene(sceneId).IsSuccess);
            Assert.True(runtime.SwitchStreamingScene(Guid.NewGuid()).IsSuccess);
        }
        finally
        {
            RemoveScenes(sceneId);
        }
    }

    [Fact]
    public async Task Native_runtime_records_several_scenes_at_once_into_separate_files()
    {
        var mediaPath = _mediaPath;
        var outputs = new List<string>();
        var runtime = _runtime;
        var programScene = Guid.NewGuid();
        var dedicatedScene = Guid.NewGuid();
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            Assert.True(runtime.CreateScene(programScene, "Program").IsSuccess);
            Assert.True(runtime.CreateScene(dedicatedScene, "Dedicated").IsSuccess);
            Assert.True(runtime.AddSource(programScene,
                new SourceAddRequest.Media(Guid.NewGuid(), "Program source", mediaPath, true)).IsSuccess);
            Assert.True(runtime.AddSource(dedicatedScene,
                new SourceAddRequest.Media(Guid.NewGuid(), "Dedicated source", mediaPath, true)).IsSuccess);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            // Two cycles: views, mixes and encoders must all be released between them.
            for (var cycle = 0; cycle < 2; cycle++)
            {
                var programPath = Path.Combine(Path.GetTempPath(), $"castor-record-{Guid.NewGuid():N}.mkv");
                var dedicatedPath = Path.Combine(Path.GetTempPath(), $"castor-record-{Guid.NewGuid():N}.mkv");
                outputs.Add(programPath);
                outputs.Add(dedicatedPath);
                var programSession = Guid.NewGuid();
                var dedicatedSession = Guid.NewGuid();

                var program = await runtime.StartRecordingAsync(
                    CreateRecordingRequest(programScene, programPath, RecordingContainer.Mkv) with { SessionId = programSession },
                    timeout.Token);
                Assert.True(program.IsSuccess, program.Message);
                var dedicated = await runtime.StartRecordingAsync(
                    CreateRecordingRequest(dedicatedScene, dedicatedPath, RecordingContainer.Mkv) with
                    {
                        SessionId = dedicatedSession,
                        FollowsProgram = false
                    },
                    timeout.Token);
                Assert.True(dedicated.IsSuccess, dedicated.Message);
                Assert.Contains("utilisée", runtime.RemoveScene(dedicatedScene).Message);

                await Task.Delay(1_000, timeout.Token);
                // Stopping one leaves the other running.
                Assert.True((await runtime.StopRecordingAsync(dedicatedSession, timeout.Token)).IsSuccess);
                Assert.True(new FileInfo(dedicatedPath).Length > 0);
                Assert.True(runtime.PauseRecording(programSession).IsSuccess);
                await Task.Delay(300, timeout.Token);
                Assert.True((await runtime.StopRecordingAsync(timeout.Token)).IsSuccess);
                Assert.True(new FileInfo(programPath).Length > 0);
            }

            Assert.Contains("WebM", (await runtime.StartRecordingAsync(
                CreateRecordingRequest(dedicatedScene, outputs[0] + ".webm", RecordingContainer.WebM) with { FollowsProgram = false },
                timeout.Token)).Message);
        }
        finally
        {
            await StopRecordingAndRemoveScenesAsync(programScene, dedicatedScene);
            foreach (var output in outputs) File.Delete(output);
        }
    }

    // The runtime is shared by every native test: each one removes what it created, even
    // when it fails, so the next test starts from an empty runtime. A scene the test
    // already removed itself just reports a failure here, which is ignored.
    private void RemoveScenes(params Guid[] sceneIds)
    {
        foreach (var sceneId in sceneIds)
            _runtime.RemoveScene(sceneId);
    }

    private async Task StopRecordingAndRemoveScenesAsync(params Guid[] sceneIds)
    {
        // A no-op unless the test failed mid-recording; a running output would otherwise
        // fail every later recording test and keep its scene undeletable.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await _runtime.StopRecordingAsync(timeout.Token);
        RemoveScenes(sceneIds);
    }

    private static RecordingRequest CreateRecordingRequest(
        Guid sceneId,
        string outputPath,
        RecordingContainer container) => new(
        sceneId,
        outputPath,
        Fps: 30,
        VideoBitrateKbps: 1_000,
        AudioBitrateKbps: 128,
        AudioSampleRate: 48_000,
        AudioChannels: 2,
        BaseWidth: 320,
        BaseHeight: 180,
        OutputWidth: 320,
        OutputHeight: 180,
        container);

    [Fact]
    public void Native_runtime_reads_back_and_changes_the_stacking_order()
    {
        var mediaPath = _mediaPath;
        var runtime = _runtime;
        var sceneId = Guid.NewGuid();
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            Assert.True(runtime.CreateScene(sceneId, $"Order {Guid.NewGuid():N}").IsSuccess);

            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            var third = Guid.NewGuid();
            foreach (var (id, name) in new[] { (first, "Une"), (second, "Deux"), (third, "Trois") })
            {
                var added = runtime.AddSource(sceneId, new SourceAddRequest.Media(id, name, mediaPath, true));
                Assert.True(added.IsSuccess, added.Message);
            }

            // An added source arrives in the foreground, so first in the order read.
            var order = runtime.GetSourceOrder(sceneId);
            Assert.True(order.IsSuccess, order.Message);
            Assert.Equal([third, second, first], order.SourceIds);

            // Rank 0 = foreground.
            Assert.True(runtime.MoveSource(sceneId, first, 0).IsSuccess);
            Assert.Equal([first, third, second], runtime.GetSourceOrder(sceneId).SourceIds);

            // Last rank = background.
            Assert.True(runtime.MoveSource(sceneId, first, 2).IsSuccess);
            Assert.Equal([third, second, first], runtime.GetSourceOrder(sceneId).SourceIds);

            Assert.True(runtime.RemoveScene(sceneId).IsSuccess);
        }
        finally
        {
            RemoveScenes(sceneId);
        }
    }


    private const uint WindowStylePopup = 0x80000000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr windowHandle);
}
