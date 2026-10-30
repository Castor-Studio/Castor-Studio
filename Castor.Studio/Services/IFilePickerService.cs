using System.Collections.Generic;
using System.Threading.Tasks;

namespace CastorApplication.Services;

public interface IFilePickerService
{
    Task<string?> PickRecordingOutputFolderAsync(string? initialPath = null);

    /// <summary>Opens a file picker to choose a video source.</summary>
    Task<string?> PickVideoFileAsync();

    /// <summary>Opens a file picker to choose an audio source.</summary>
    Task<string?> PickAudioFileAsync();

    /// <summary>Opens a file picker to choose an audio or video media source.</summary>
    Task<string?> PickMediaFileAsync();

    /// <summary>Opens a save picker to export scenes as JSON.</summary>
    Task<string?> PickSceneExportFileAsync();

    /// <summary>Opens a file picker to import scenes from one or more JSON files. Empty if cancelled.</summary>
    Task<IReadOnlyList<string>> PickSceneImportFilesAsync();
}
