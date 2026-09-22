using CastorApplication.Models.Settings;
using CastorApplication.Services.Settings;
using CastorApplication.ViewModels.Settings.Sections;

namespace Castor.Studio.Tests;

public sealed class VideoSettingsViewModelTests
{
    [Fact]
    public void Missing_settings_select_the_monitor_option_and_save_its_dimensions()
    {
        var directory = Directory.CreateTempSubdirectory("castor-video-settings-");
        try
        {
            var settingsService = new SettingsService(Path.Combine(directory.FullName, "settings.json"));
            var resolver = new VideoCanvasResolutionResolver(
                settingsService,
                new TestResolutionProvider(new VideoCanvasResolution(3440, 1440)));
            var viewModel = new VideoSettingsViewModel(resolver);

            viewModel.Load(settingsService.Load());

            Assert.Equal(new VideoCanvasResolution(3440, 1440), viewModel.SelectedBaseResolution!.Resolution);
            Assert.Contains(viewModel.BaseResolutionOptions, option => option.LegacyIndex == 3);

            var saved = new ApplicationSettings();
            viewModel.Save(saved);

            Assert.Equal(3440, saved.BaseCanvasWidth);
            Assert.Equal(1440, saved.BaseCanvasHeight);
            Assert.Equal(-1, saved.SelectedBaseResolutionIndex);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Selecting_a_legacy_preset_keeps_its_compatible_index()
    {
        var directory = Directory.CreateTempSubdirectory("castor-video-settings-");
        try
        {
            var settingsService = new SettingsService(Path.Combine(directory.FullName, "settings.json"));
            var resolver = new VideoCanvasResolutionResolver(
                settingsService,
                new TestResolutionProvider(new VideoCanvasResolution(3440, 1440)));
            var viewModel = new VideoSettingsViewModel(resolver);
            viewModel.Load(settingsService.Load());
            viewModel.SelectedBaseResolution = viewModel.BaseResolutionOptions
                .Single(option => option.LegacyIndex == 3);

            var saved = new ApplicationSettings();
            viewModel.Save(saved);

            Assert.Equal(3, saved.SelectedBaseResolutionIndex);
            Assert.Equal(2560, saved.BaseCanvasWidth);
            Assert.Equal(1440, saved.BaseCanvasHeight);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
