using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using CastorApplication.Models.Settings;

namespace CastorApplication.Services.Settings;

public sealed class SettingsService
{
    private const string CurrentAppFolderName = "castor-studio";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsFilePath;

    public SettingsService(string? settingsFilePath = null)
    {
        _settingsFilePath = settingsFilePath ?? BuildDefaultSettingsPath(CurrentAppFolderName);
    }

    public ApplicationSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                return new ApplicationSettings();
            }

            var json = File.ReadAllText(_settingsFilePath);
            try
            {
                return JsonSerializer.Deserialize<ApplicationSettings>(json, JsonOptions) ?? new ApplicationSettings();
            }
            catch (JsonException ex)
            {
                BackupCorruptSettingsFile();
                LogError("Failed to deserialize settings JSON.", ex);
                return new ApplicationSettings();
            }
        }
        catch (Exception ex)
        {
            LogError("Failed to load settings from disk.", ex);
            return new ApplicationSettings();
        }
    }

    public void Save(ApplicationSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        WriteFileAtomically(_settingsFilePath, json);
    }

    private void BackupCorruptSettingsFile()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                return;
            }

            var backupPath = $"{_settingsFilePath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Copy(_settingsFilePath, backupPath, overwrite: false);
        }
        catch (Exception ex)
        {
            LogError("Failed to back up corrupt settings file.", ex);
        }
    }

    internal static void WriteFileAtomically(string destinationPath, string content)
    {
        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("Settings destination directory is invalid.");

        var tempPath = Path.Combine(directory, $"{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tempPath, content);

        try
        {
            File.Move(tempPath, destinationPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }

    private static void LogError(string message, Exception ex)
    {
        Debug.WriteLine($"[SettingsService] {message} {ex.GetType().Name}: {ex.Message}");
    }

    private static string BuildDefaultSettingsPath(string appFolderName)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, appFolderName, "settings.json");
    }
}
