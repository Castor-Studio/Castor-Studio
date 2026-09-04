using System.Threading.Tasks;

namespace CastorApplication.Services;

public interface IFilePickerService
{
    Task<string?> PickRecordingOutputFolderAsync(string? initialPath = null);

    /// <summary>Ouvre un sélecteur de fichier pour choisir une source vidéo.</summary>
    Task<string?> PickVideoFileAsync();

    /// <summary>Ouvre un sélecteur de fichier pour choisir une source audio.</summary>
    Task<string?> PickAudioFileAsync();

    /// <summary>Ouvre un sélecteur de fichier pour choisir une source média audio ou vidéo.</summary>
    Task<string?> PickMediaFileAsync();

    /// <summary>Ouvre un sélecteur d'enregistrement pour exporter des scènes en JSON.</summary>
    Task<string?> PickSceneExportFileAsync();

    /// <summary>Ouvre un sélecteur de fichier pour importer des scènes depuis un JSON.</summary>
    Task<string?> PickSceneImportFileAsync();
}
