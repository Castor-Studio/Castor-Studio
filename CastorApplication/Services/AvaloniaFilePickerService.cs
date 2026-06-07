using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace CastorApplication.Services;

public sealed class AvaloniaFilePickerService : IFilePickerService
{
    public async Task<string?> PickRecordingOutputFileAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        var topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
        if (topLevel == null) return null;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Enregistrer la vidéo sous...",
            SuggestedFileName = $"Castor_{DateTime.Now:yyyyMMdd_HHmmss}",
            FileTypeChoices =
            [
                new FilePickerFileType("MP4 (H.264 + AAC)") { Patterns = ["*.mp4"] },
                new FilePickerFileType("MKV (H.264 + AAC)") { Patterns = ["*.mkv"] },
            ]
        });

        return file?.Path.LocalPath;
    }
}
