using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace CastorApplication.Services;

public sealed class AvaloniaFilePickerService : IFilePickerService
{
    public async Task<string?> PickRecordingOutputFileAsync(
        string extension   = ".mp4",
        string formatLabel = "MP4 (H.264 + AAC)")
    {
        if (Application.Current?.ApplicationLifetime
            is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        var topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
        if (topLevel == null) return null;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title             = "Enregistrer la vidéo sous...",
            SuggestedFileName = $"Castor_{DateTime.Now:yyyyMMdd_HHmmss}{extension}",
            FileTypeChoices   =
            [
                new FilePickerFileType(formatLabel) { Patterns = [$"*{extension}"] },
            ]
        });

        // Garantir que le chemin retourné a la bonne extension
        var path = file?.Path.LocalPath;
        if (path != null && !path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            path += extension;

        return path;
    }

    public async Task<string?> PickVideoFileAsync()
    {
        if (Application.Current?.ApplicationLifetime
            is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        var topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
        if (topLevel == null) return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choisir un fichier vidéo",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Fichiers vidéo")
                {
                    Patterns = ["*.mp4", "*.mkv", "*.mov", "*.avi", "*.webm"]
                },
                new FilePickerFileType("Tous les fichiers") { Patterns = ["*.*"] }
            ]
        });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    public async Task<string?> PickAudioFileAsync()
    {
        if (Application.Current?.ApplicationLifetime
            is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        var topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
        if (topLevel == null) return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choisir un fichier audio",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Fichiers audio")
                {
                    Patterns = ["*.mp3", "*.wav", "*.aac", "*.ogg", "*.flac", "*.m4a"]
                },
                // Les fichiers vidéo contiennent souvent une piste audio exploitable
                new FilePickerFileType("Fichiers vidéo (piste audio)")
                {
                    Patterns = ["*.mp4", "*.mkv", "*.mov", "*.avi", "*.webm"]
                },
                new FilePickerFileType("Tous les fichiers") { Patterns = ["*.*"] }
            ]
        });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    public async Task<string?> PickSceneExportFileAsync()
    {
        if (Application.Current?.ApplicationLifetime
            is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        var topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
        if (topLevel == null) return null;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title             = "Exporter les scènes",
            SuggestedFileName = $"Castor_Scenes_{DateTime.Now:yyyyMMdd_HHmmss}.json",
            FileTypeChoices   =
            [
                new FilePickerFileType("Scènes Castor (JSON)") { Patterns = ["*.json"] },
            ]
        });

        var path = file?.Path.LocalPath;
        if (path != null && !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            path += ".json";

        return path;
    }

    public async Task<string?> PickSceneImportFileAsync()
    {
        if (Application.Current?.ApplicationLifetime
            is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        var topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
        if (topLevel == null) return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Importer des scènes",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Scènes Castor (JSON)") { Patterns = ["*.json"] },
            ]
        });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }
}
