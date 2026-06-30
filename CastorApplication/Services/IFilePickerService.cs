using System.Threading.Tasks;

namespace CastorApplication.Services;

public interface IFilePickerService
{
    Task<string?> PickRecordingOutputFileAsync(
        string extension  = ".mp4",
        string formatLabel = "MP4 (H.264 + AAC)");

    /// <summary>Ouvre un sélecteur de fichier pour choisir une source vidéo.</summary>
    Task<string?> PickVideoFileAsync();

    /// <summary>Ouvre un sélecteur de fichier pour choisir une source audio.</summary>
    Task<string?> PickAudioFileAsync();
}
