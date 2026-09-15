using CastorApplication.Models.Studio;
using CastorApplication.Services.Studio;
using LibObs;
using System.Runtime.InteropServices;

namespace Castor.Studio.Tests;

public sealed class LibObsSceneRuntimeTests
{
    [Fact]
    public void Streaming_request_requires_a_twitch_key()
    {
        var request = new StreamingRequest(
            new SceneDefinition(),
            "",
            30,
            6_000,
            192,
            48_000,
            2);

        Assert.Contains("clé de stream Twitch", LibObsSceneRuntime.ValidateStreamingRequest(request));
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
            "live-key",
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
            "live-key",
            30,
            6_000,
            192,
            48_000,
            2);

        Assert.Empty(LibObsSceneRuntime.ValidateStreamingRequest(request));
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
    public void Native_scene_lifecycle_deduplicates_names_and_shuts_down_cleanly()
    {
        var runtime = new LibObsSceneRuntime();
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            var requestedName = $"Castor test {Guid.NewGuid():N}";
            var firstId = Guid.NewGuid();
            var secondId = Guid.NewGuid();

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
            runtime.Dispose();
        }

        Assert.False(Obs.IsInitialized);
    }

    [Fact]
    public async Task Native_runtime_enumerates_and_manages_a_media_source()
    {
        var mediaPath = Path.Combine(Path.GetTempPath(), $"castor-media-{Guid.NewGuid():N}.wav");
        WriteSilentWave(mediaPath);
        var runtime = new LibObsSceneRuntime();
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            var catalog = await runtime.EnumerateSourcesAsync(CancellationToken.None);
            Assert.NotNull(catalog.VideoSources);
            Assert.NotNull(catalog.AudioSources);
            Assert.True(string.IsNullOrEmpty(catalog.Message), catalog.Message);

            var sceneId = Guid.NewGuid();
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
            runtime.Dispose();
            File.Delete(mediaPath);
        }

        Assert.False(Obs.IsInitialized);
    }

    [Fact]
    public async Task Native_runtime_adds_and_removes_a_monitor_source()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var runtime = new LibObsSceneRuntime();
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

        using var runtime = new LibObsSceneRuntime();
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
        var mediaPath = Path.Combine(Path.GetTempPath(), $"castor-record-source-{Guid.NewGuid():N}.wav");
        WriteSilentWave(mediaPath);
        var runtime = new LibObsSceneRuntime();
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            var sceneId = Guid.NewGuid();
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
            runtime.Dispose();
            File.Delete(outputPath);
            File.Delete(mediaPath);
        }

        Assert.False(Obs.IsInitialized);
    }

    [Fact]
    public async Task Native_runtime_supports_two_recordings_in_sequence()
    {
        var firstPath = Path.Combine(Path.GetTempPath(), $"castor-record-{Guid.NewGuid():N}.mkv");
        var secondPath = Path.Combine(Path.GetTempPath(), $"castor-record-{Guid.NewGuid():N}.mkv");
        var mediaPath = Path.Combine(Path.GetTempPath(), $"castor-record-source-{Guid.NewGuid():N}.wav");
        WriteSilentWave(mediaPath);
        var runtime = new LibObsSceneRuntime();
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            var sceneId = Guid.NewGuid();
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
            runtime.Dispose();
            File.Delete(firstPath);
            File.Delete(secondPath);
            File.Delete(mediaPath);
        }
    }

    [Fact]
    public async Task Native_runtime_rejects_an_unknown_or_empty_scene()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"castor-record-{Guid.NewGuid():N}.mp4");
        var runtime = new LibObsSceneRuntime();
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            var unknown = await runtime.StartRecordingAsync(
                CreateRecordingRequest(Guid.NewGuid(), outputPath, RecordingContainer.Mp4), timeout.Token);
            Assert.False(unknown.IsSuccess);
            Assert.Contains("n'existe pas", unknown.Message);

            var emptySceneId = Guid.NewGuid();
            Assert.True(runtime.CreateScene(emptySceneId, "Empty record").IsSuccess);
            var empty = await runtime.StartRecordingAsync(
                CreateRecordingRequest(emptySceneId, outputPath, RecordingContainer.Mp4), timeout.Token);
            Assert.False(empty.IsSuccess);
            Assert.Contains("source vidéo ou média", empty.Message);
        }
        finally
        {
            runtime.Dispose();
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task Native_preview_rejects_an_invalid_surface_and_unknown_scene()
    {
        var runtime = new LibObsSceneRuntime();
        try
        {
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
        finally
        {
            runtime.Dispose();
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

        var mediaPath = Path.Combine(Path.GetTempPath(), $"castor-preview-source-{Guid.NewGuid():N}.wav");
        WriteSilentWave(mediaPath);
        var runtime = new LibObsSceneRuntime();
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            var scene = new SceneDefinition { Name = "Empty preview" };
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
            runtime.Dispose();
            DestroyWindow(windowHandle);
            File.Delete(mediaPath);
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

        var runtime = new LibObsSceneRuntime();
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            var scene = new SceneDefinition { Name = "Shared preview" };
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
            runtime.Dispose();
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

        var runtime = new LibObsSceneRuntime();
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            var scene = new SceneDefinition { Name = "Removed while shown twice" };
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
            runtime.Dispose();
            DestroyWindow(firstWindow);
            DestroyWindow(secondWindow);
        }
    }

    [Fact]
    public async Task Switching_scene_mid_recording_re_points_the_live_output()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"castor-switch-{Guid.NewGuid():N}.mkv");
        var mediaPath = Path.Combine(Path.GetTempPath(), $"castor-switch-source-{Guid.NewGuid():N}.wav");
        WriteSilentWave(mediaPath);
        var runtime = new LibObsSceneRuntime();
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
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
            runtime.Dispose();
            File.Delete(outputPath);
            File.Delete(mediaPath);
        }
    }

    [Fact]
    public void Switching_scene_without_a_recording_is_a_no_op()
    {
        var runtime = new LibObsSceneRuntime();
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            var sceneId = Guid.NewGuid();
            Assert.True(runtime.CreateScene(sceneId, "Hors enregistrement").IsSuccess);

            // Nothing is running, so there is no output to re-point - and an unknown scene
            // is not reported as an error either, since nothing was asked of LibObs.
            Assert.True(runtime.SwitchRecordingScene(sceneId).IsSuccess);
            Assert.True(runtime.SwitchRecordingScene(Guid.NewGuid()).IsSuccess);
        }
        finally
        {
            runtime.Dispose();
        }
    }

    [Fact]
    public void Switching_scene_without_a_live_is_a_no_op()
    {
        var runtime = new LibObsSceneRuntime();
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            var sceneId = Guid.NewGuid();
            Assert.True(runtime.CreateScene(sceneId, "Hors live").IsSuccess);

            // Nothing is running, so there is no output to re-point - and an unknown scene
            // is not reported as an error either, since nothing was asked of LibObs.
            Assert.True(runtime.SwitchStreamingScene(sceneId).IsSuccess);
            Assert.True(runtime.SwitchStreamingScene(Guid.NewGuid()).IsSuccess);
        }
        finally
        {
            runtime.Dispose();
        }
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
        var mediaPath = Path.Combine(Path.GetTempPath(), $"castor-order-{Guid.NewGuid():N}.wav");
        WriteSilentWave(mediaPath);
        var runtime = new LibObsSceneRuntime();
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            var sceneId = Guid.NewGuid();
            Assert.True(runtime.CreateScene(sceneId, $"Order {Guid.NewGuid():N}").IsSuccess);

            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            var third = Guid.NewGuid();
            foreach (var (id, name) in new[] { (first, "Une"), (second, "Deux"), (third, "Trois") })
            {
                var added = runtime.AddSource(sceneId, new SourceAddRequest.Media(id, name, mediaPath, true));
                Assert.True(added.IsSuccess, added.Message);
            }

            // Une source ajoutée arrive au premier plan, donc en tête de l'ordre lu.
            var order = runtime.GetSourceOrder(sceneId);
            Assert.True(order.IsSuccess, order.Message);
            Assert.Equal([third, second, first], order.SourceIds);

            // Rang 0 = premier plan.
            Assert.True(runtime.MoveSource(sceneId, first, 0).IsSuccess);
            Assert.Equal([first, third, second], runtime.GetSourceOrder(sceneId).SourceIds);

            // Dernier rang = arrière-plan.
            Assert.True(runtime.MoveSource(sceneId, first, 2).IsSuccess);
            Assert.Equal([third, second, first], runtime.GetSourceOrder(sceneId).SourceIds);

            Assert.True(runtime.RemoveScene(sceneId).IsSuccess);
        }
        finally
        {
            runtime.Dispose();
            File.Delete(mediaPath);
        }
    }

    private static void WriteSilentWave(string path)
    {
        const int sampleRate = 8_000;
        const short channels = 1;
        const short bitsPerSample = 16;
        var data = new byte[sampleRate * channels * bitsPerSample / 8 / 10];

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + data.Length);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8);
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write(bitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(data.Length);
        writer.Write(data);
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
