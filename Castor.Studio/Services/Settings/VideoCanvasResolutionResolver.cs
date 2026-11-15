using CastorApplication.Models.Settings;
using CastorApplication.Services.Platform;

namespace CastorApplication.Services.Settings;

internal sealed class VideoCanvasResolutionResolver
{
    private readonly SettingsService? _settingsService;
    private readonly IPrimaryMonitorResolutionProvider _monitorResolutionProvider;

    public VideoCanvasResolutionResolver(
        SettingsService? settingsService = null,
        IPrimaryMonitorResolutionProvider? monitorResolutionProvider = null)
    {
        _settingsService = settingsService;
        _monitorResolutionProvider = monitorResolutionProvider ?? new WindowsPrimaryMonitorResolutionProvider();
    }

    public VideoCanvasResolution Resolve(ApplicationSettings settings)
    {
        if (settings.BaseCanvasWidth is int width &&
            settings.BaseCanvasHeight is int height)
        {
            var explicitResolution = new VideoCanvasResolution(width, height);
            if (explicitResolution.IsValid) return explicitResolution;
        }

        if (_settingsService?.HasPersistedSettings == true)
        {
            var legacyResolution = VideoResolution.BaseFromIndex(settings.SelectedBaseResolutionIndex);
            return new VideoCanvasResolution(legacyResolution.Width, legacyResolution.Height);
        }

        return _monitorResolutionProvider.GetPrimaryMonitorResolution() is { IsValid: true } detected
            ? detected
            : new VideoCanvasResolution(1920, 1080);
    }

    public IReadOnlyList<BaseResolutionOption> GetBaseOptions(ApplicationSettings settings)
    {
        var monitorResolution = _monitorResolutionProvider.GetPrimaryMonitorResolution();
        if (!monitorResolution.IsValid)
            monitorResolution = new VideoCanvasResolution(1920, 1080);
        var options = VideoResolution.BaseOptions(monitorResolution).ToList();
        var selectedResolution = Resolve(settings);

        if (options.All(option => option.Resolution != selectedResolution))
        {
            options.Insert(0, new(
                $"{selectedResolution.Width}x{selectedResolution.Height} (Configuration actuelle)",
                selectedResolution,
                null));
        }

        return options;
    }
}
