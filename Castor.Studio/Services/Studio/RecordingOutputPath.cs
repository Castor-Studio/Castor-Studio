using CastorApplication.Models.Settings;
using CastorApplication.Models.Studio;

namespace CastorApplication.Services.Studio;

internal static class RecordingOutputPath
{
    public static string Create(
        string? configuredDirectory,
        RecordingContainer container,
        DateTime timestamp)
    {
        var directory = string.IsNullOrWhiteSpace(configuredDirectory)
            ? ApplicationSettings.DefaultOutputPath
            : configuredDirectory.Trim();

        if (!Path.IsPathFullyQualified(directory))
            throw new InvalidOperationException("Le dossier de sortie doit être un chemin absolu.");

        try
        {
            directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(directory);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new InvalidOperationException($"Le dossier de sortie n'est pas accessible : {exception.Message}", exception);
        }

        var extension = container switch
        {
            RecordingContainer.Mp4 => ".mp4",
            RecordingContainer.Mkv => ".mkv",
            RecordingContainer.WebM => ".webm",
            _ => throw new ArgumentOutOfRangeException(nameof(container))
        };
        var baseName = $"Castor_{timestamp:yyyyMMdd_HHmmss_fff}";
        var path = Path.Combine(directory, baseName + extension);
        for (var suffix = 2; File.Exists(path); suffix++)
            path = Path.Combine(directory, $"{baseName}_{suffix}{extension}");

        return path;
    }
}
