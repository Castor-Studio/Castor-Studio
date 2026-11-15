using System.Collections.ObjectModel;
using CastorApplication.Models.Settings;
using CastorApplication.Services.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CastorApplication.ViewModels.Settings.Sections;

public partial class VideoSettingsViewModel : SettingsSectionViewModel
{
    private readonly VideoCanvasResolutionResolver _resolutionResolver;

    public ObservableCollection<BaseResolutionOption> BaseResolutionOptions { get; } = [];

    [ObservableProperty]
    private BaseResolutionOption? _selectedBaseResolution;

    [ObservableProperty]
    private int _selectedBaseResolutionIndex = 1;

    [ObservableProperty]
    private int _selectedOutputResolutionIndex;

    [ObservableProperty]
    private int _selectedFpsIndex;

    [ObservableProperty]
    private double _videoBitrate = 6000;

    internal VideoSettingsViewModel(VideoCanvasResolutionResolver? resolutionResolver = null)
    {
        _resolutionResolver = resolutionResolver ?? new VideoCanvasResolutionResolver();
    }

    public string VideoBitrateDisplay => $"{(int)VideoBitrate}";

    partial void OnVideoBitrateChanged(double value)
        => OnPropertyChanged(nameof(VideoBitrateDisplay));

    protected override void LoadCore(ApplicationSettings settings)
    {
        BaseResolutionOptions.Clear();
        foreach (var option in _resolutionResolver.GetBaseOptions(settings))
            BaseResolutionOptions.Add(option);

        var resolvedBaseResolution = _resolutionResolver.Resolve(settings);
        SelectedBaseResolution = BaseResolutionOptions.FirstOrDefault(
            option => option.Resolution == resolvedBaseResolution);
        SelectedBaseResolutionIndex = SelectedBaseResolution?.LegacyIndex
            ?? settings.SelectedBaseResolutionIndex;
        SelectedOutputResolutionIndex = settings.SelectedOutputResolutionIndex;
        SelectedFpsIndex = settings.SelectedFpsIndex;
        VideoBitrate = settings.VideoBitrate;
    }

    protected override void SaveCore(ApplicationSettings settings)
    {
        if (SelectedBaseResolution != null)
        {
            settings.SelectedBaseResolutionIndex = SelectedBaseResolution.LegacyIndex ?? -1;
            settings.BaseCanvasWidth = SelectedBaseResolution.Resolution.Width;
            settings.BaseCanvasHeight = SelectedBaseResolution.Resolution.Height;
        }
        else
        {
            settings.SelectedBaseResolutionIndex = SelectedBaseResolutionIndex;
        }
        settings.SelectedOutputResolutionIndex = SelectedOutputResolutionIndex;
        settings.SelectedFpsIndex = SelectedFpsIndex;
        settings.VideoBitrate = VideoBitrate;
    }
}
