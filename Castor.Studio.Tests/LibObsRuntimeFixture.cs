using CastorApplication.Services.Settings;
using CastorApplication.Services.Studio;
using LibObs;

namespace Castor.Studio.Tests;

// One libobs lifecycle for every native test, like the app (one runtime per process).
// libobs never unloads plugin DLLs on shutdown, so their static state survives a
// restart: win-capture then stops waiting for its graphics-offsets thread in
// obs_module_unload, and obs_shutdown frees the module under that thread - a native
// crash of the test host. Starting libobs once avoids restarting it at all.
public sealed class LibObsRuntimeFixture : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"castor-libobs-tests-{Guid.NewGuid():N}");

    public LibObsRuntimeFixture()
    {
        Directory.CreateDirectory(_directory);
        MediaPath = Path.Combine(_directory, "silence.wav");
        VideoPath = Path.Combine(_directory, "clip.y4m");
        WriteSilentWave(MediaPath);
        WriteTestVideo(VideoPath, 64, 36);
        Settings = new SettingsService(Path.Combine(_directory, "settings.json"));
        Runtime = new LibObsSceneRuntime(Settings);
    }

    internal LibObsSceneRuntime Runtime { get; }

    // The runtime reloads its transition when these are saved, as in the app.
    internal SettingsService Settings { get; }

    // Shared by every media source: libobs closes a removed source's file on its own
    // threads, so a per-test file could still be open when the test deletes it. These are
    // deleted once libobs has shut down.
    public string MediaPath { get; }
    public string VideoPath { get; }

    public void Dispose()
    {
        // Obs.Shutdown throws if a test leaked a native handle; reported as a fixture
        // cleanup failure.
        Runtime.Dispose();
        Directory.Delete(_directory, recursive: true);
        if (Obs.IsInitialized)
            throw new InvalidOperationException("LibObs est toujours initialisé après l'arrêt du runtime.");
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

    // One second of raw YUV 4:2:0 video: FFmpeg reads it with no codec or container to build.
    private static void WriteTestVideo(string path, int width, int height)
    {
        const int frames = 30;
        using var stream = File.Create(path);
        stream.Write(System.Text.Encoding.ASCII.GetBytes($"YUV4MPEG2 W{width} H{height} F30:1 Ip A1:1 C420jpeg\n"));
        var luma = new byte[width * height];
        var chroma = new byte[width / 2 * (height / 2)];
        Array.Fill(chroma, (byte)128);
        for (var frame = 0; frame < frames; frame++)
        {
            Array.Fill(luma, (byte)(40 + frame * 6));
            stream.Write("FRAME\n"u8);
            stream.Write(luma);
            stream.Write(chroma);
            stream.Write(chroma);
        }
    }
}

[CollectionDefinition(Name)]
public sealed class LibObsCollection : ICollectionFixture<LibObsRuntimeFixture>
{
    public const string Name = "LibObs native";
}
