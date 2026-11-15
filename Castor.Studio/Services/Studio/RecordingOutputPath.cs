using CastorApplication.Models.Settings;
using CastorApplication.Models.Studio;

namespace CastorApplication.Services.Studio;

internal static class RecordingOutputPath
{
    // sceneName: set for a scene recorded on its own, so simultaneous files say which scene
    // they hold. taken: paths already handed out for recordings starting together, whose
    // files do not exist yet.
    public static string Create(
        string? configuredDirectory,
        RecordingContainer container,
        DateTime timestamp,
        string? sceneName = null,
        IReadOnlySet<string>? taken = null)
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
        if (sceneName != null) baseName += $"_{FileNamePart(sceneName)}";
        var path = Path.Combine(directory, baseName + extension);
        for (var suffix = 2; File.Exists(path) || taken?.Contains(path) == true; suffix++)
            path = Path.Combine(directory, $"{baseName}_{suffix}{extension}");

        return path;
    }

    private const int MaxSceneNameLength = 60;

    private static string FileNamePart(string sceneName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(sceneName.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim('.', ' ');
        if (cleaned.Length > MaxSceneNameLength) cleaned = cleaned[..MaxSceneNameLength].TrimEnd('.', ' ');
        return cleaned.Length == 0 ? "Scene" : cleaned;
    }
}
