using CastorApplication.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CastorApplication.ViewModels.Settings;

public abstract partial class SettingsSectionViewModel : ObservableObject, ISettingsSection
{
    private bool _isLoading;

    [ObservableProperty]
    private bool _isDirty;

    protected SettingsSectionViewModel()
    {
        PropertyChanged += (_, e) =>
        {
            if (_isLoading || e.PropertyName is null || e.PropertyName == nameof(IsDirty))
            {
                return;
            }

            IsDirty = true;
        };
    }

    public void Load(ApplicationSettings settings)
    {
        _isLoading = true;
        try
        {
            LoadCore(settings);
            IsDirty = false;
        }
        finally
        {
            _isLoading = false;
        }
    }

    public void Save(ApplicationSettings settings)
    {
        SaveCore(settings);
    }

    public void MarkClean()
    {
        IsDirty = false;
    }

    protected abstract void LoadCore(ApplicationSettings settings);
    protected abstract void SaveCore(ApplicationSettings settings);
}
