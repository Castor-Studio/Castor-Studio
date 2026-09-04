using CastorApplication.Models.Settings;
using CastorApplication.Models.Studio;
using CastorApplication.Services.Settings;
using CastorApplication.Services.Studio;

namespace Castor.Studio.Tests;

public sealed class RecordingOutputPathTests
{
    [Theory]
    [InlineData(RecordingContainer.Mp4, ".mp4")]
    [InlineData(RecordingContainer.Mkv, ".mkv")]
    [InlineData(RecordingContainer.WebM, ".webm")]
    public void Create_builds_a_unique_path_with_the_selected_extension(
        RecordingContainer container,
        string extension)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var timestamp = new DateTime(2026, 9, 3, 16, 30, 42, 123);
            var first = RecordingOutputPath.Create(directory, container, timestamp);
            File.WriteAllBytes(first, [1]);
            var second = RecordingOutputPath.Create(directory, container, timestamp);

            Assert.Equal(Path.Combine(directory, $"Castor_20260903_163042_123{extension}"), first);
            Assert.Equal(Path.Combine(directory, $"Castor_20260903_163042_123_2{extension}"), second);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Create_rejects_a_relative_directory()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            RecordingOutputPath.Create("relative", RecordingContainer.Mp4, DateTime.Now));

        Assert.Contains("absolu", exception.Message);
    }

    [Fact]
    public void Create_creates_a_missing_directory()
    {
        var root = CreateTemporaryDirectory();
        var directory = Path.Combine(root, "nouveau", "videos");
        try
        {
            var path = RecordingOutputPath.Create(directory, RecordingContainer.Mkv, DateTime.Now);

            Assert.True(Directory.Exists(directory));
            Assert.Equal(directory, Path.GetDirectoryName(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Create_reports_an_inaccessible_directory()
    {
        var root = CreateTemporaryDirectory();
        var file = Path.Combine(root, "not-a-directory");
        File.WriteAllText(file, "occupied");
        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                RecordingOutputPath.Create(Path.Combine(file, "videos"), RecordingContainer.Mp4, DateTime.Now));

            Assert.Contains("pas accessible", exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Application_settings_default_to_the_windows_videos_directory()
    {
        var settings = new ApplicationSettings();

        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), settings.OutputPath);
    }

    [Fact]
    public void Legacy_quality_setting_is_ignored_when_loading_settings()
    {
        var directory = CreateTemporaryDirectory();
        var settingsPath = Path.Combine(directory, "settings.json");
        try
        {
            File.WriteAllText(settingsPath,
                """{"RecordingQualityIndex":0,"OutputPath":"C:\\Videos","VideoBitrate":7500}""");

            var settings = new SettingsService(settingsPath).Load();

            Assert.Equal(@"C:\Videos", settings.OutputPath);
            Assert.Equal(7_500, settings.VideoBitrate);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Empty_saved_output_path_falls_back_to_the_windows_videos_directory()
    {
        var directory = CreateTemporaryDirectory();
        var settingsPath = Path.Combine(directory, "settings.json");
        try
        {
            File.WriteAllText(settingsPath, """{"OutputPath":""}""");

            var settings = new SettingsService(settingsPath).Load();

            Assert.Equal(ApplicationSettings.DefaultOutputPath, settings.OutputPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"castor-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
