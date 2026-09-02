using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Dock.Model.Core;
using Dock.Serializer.SystemTextJson;

namespace CastorApplication.Services.Settings;

public sealed class DockLayoutService
{
    private const string CurrentAppFolderName = "castor-studio";

    private readonly string _layoutFilePath;
    private readonly DockSerializer _serializer = new();

    public DockLayoutService(string? layoutFilePath = null)
    {
        _layoutFilePath = layoutFilePath ?? BuildDefaultLayoutPath(CurrentAppFolderName);
    }

    public IDock? Load()
    {
        try
        {
            if (!File.Exists(_layoutFilePath))
            {
                return null;
            }

            var json = File.ReadAllText(_layoutFilePath);
            try
            {
                return _serializer.Deserialize<IDock?>(json);
            }
            catch (JsonException ex)
            {
                BackupCorruptLayoutFile();
                LogError("Failed to deserialize dock layout JSON.", ex);
                return null;
            }
        }
        catch (Exception ex)
        {
            LogError("Failed to load dock layout from disk.", ex);
            return null;
        }
    }

    public void Save(IDock layout)
    {
        var directory = Path.GetDirectoryName(_layoutFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = _serializer.Serialize(layout);
        SettingsService.WriteFileAtomically(_layoutFilePath, json);
    }

    private void BackupCorruptLayoutFile()
    {
        try
        {
            if (!File.Exists(_layoutFilePath))
            {
                return;
            }

            var backupPath = $"{_layoutFilePath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Copy(_layoutFilePath, backupPath, overwrite: false);
        }
        catch (Exception ex)
        {
            LogError("Failed to back up corrupt dock layout file.", ex);
        }
    }

    private static void LogError(string message, Exception ex)
    {
        Debug.WriteLine($"[DockLayoutService] {message} {ex.GetType().Name}: {ex.Message}");
    }

    private static string BuildDefaultLayoutPath(string appFolderName)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, appFolderName, "dock-layout.json");
    }
}
