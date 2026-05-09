using CastorApplication.Models;

namespace CastorApplication.ViewModels.Settings.Sections;

public interface ISettingsSection
{
    void Load(ApplicationSettings settings);
    void Save(ApplicationSettings settings);
    bool IsDirty { get; }
    void MarkClean();
}