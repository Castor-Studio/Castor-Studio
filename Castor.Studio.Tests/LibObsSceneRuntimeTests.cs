using CastorApplication.Services.Studio;
using LibObs;

namespace Castor.Studio.Tests;

public sealed class LibObsSceneRuntimeTests
{
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
}
