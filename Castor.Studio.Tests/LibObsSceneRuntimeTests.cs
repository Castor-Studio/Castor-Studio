using CastorApplication.Models.Studio;
using CastorApplication.Services.Studio;
using LibObs;
using System.Runtime.InteropServices;

namespace Castor.Studio.Tests;

public sealed class LibObsSceneRuntimeTests
{
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
            Assert.True((await runtime.StopPreviewAsync(scene.Id, timeout.Token)).IsSuccess);
            Assert.True(runtime.RemoveScene(scene.Id).IsSuccess);
        }
        finally
        {
            runtime.Dispose();
            DestroyWindow(windowHandle);
            File.Delete(mediaPath);
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
