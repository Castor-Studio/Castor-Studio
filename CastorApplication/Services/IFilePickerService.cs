using System.Threading.Tasks;

namespace CastorApplication.Services;

public interface IFilePickerService
{
    Task<string?> PickRecordingOutputFileAsync(
        string extension  = ".mp4",
        string formatLabel = "MP4 (H.264 + AAC)");
}
