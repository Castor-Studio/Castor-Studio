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
}
