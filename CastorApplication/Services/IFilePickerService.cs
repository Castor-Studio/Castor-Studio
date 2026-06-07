using System.Threading.Tasks;

namespace CastorApplication.Services;

public interface IFilePickerService
{
    Task<string?> PickRecordingOutputFileAsync();
}
