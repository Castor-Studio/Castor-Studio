using CastorApplication.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CastorApplication.ViewModels.Settings;

public partial class VideoSettingsSectionViewModel : SettingsSectionViewModel
{
    [ObservableProperty]
    private int _selectedBaseResolutionIndex = 1;

    [ObservableProperty]
    private int _selectedOutputResolutionIndex;

    [ObservableProperty]
    private int _selectedFpsIndex;

    [ObservableProperty]
    private double _videoBitrate = 6000;

    public string VideoBitrateDisplay => $"{(int)VideoBitrate}";

    partial void OnVideoBitrateChanged(double value)
    {
        OnPropertyChanged(nameof(VideoBitrateDisplay));
    }

    protected override void LoadCore(ApplicationSettings settings)
    {
        SelectedBaseResolutionIndex = settings.SelectedBaseResolutionIndex;
        SelectedOutputResolutionIndex = settings.SelectedOutputResolutionIndex;
        SelectedFpsIndex = settings.SelectedFpsIndex;
        VideoBitrate = settings.VideoBitrate;
    }

    protected override void SaveCore(ApplicationSettings settings)
    {
        settings.SelectedBaseResolutionIndex = SelectedBaseResolutionIndex;
        settings.SelectedOutputResolutionIndex = SelectedOutputResolutionIndex;
        settings.SelectedFpsIndex = SelectedFpsIndex;
        settings.VideoBitrate = VideoBitrate;
    }
}
