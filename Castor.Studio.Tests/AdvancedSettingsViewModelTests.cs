using CastorApplication.Models.Settings;
using CastorApplication.ViewModels.Settings.Sections;

namespace Castor.Studio.Tests;

public sealed class AdvancedSettingsViewModelTests
{
    [Fact]
    public void The_ai_device_defaults_to_auto()
    {
        var viewModel = new AdvancedSettingsViewModel();

        viewModel.Load(new ApplicationSettings());

        Assert.Equal((int)AiDevice.Auto, viewModel.SelectedAiDeviceIndex);
    }

    [Theory]
    [InlineData(AiDevice.Auto)]
    [InlineData(AiDevice.Cpu)]
    [InlineData(AiDevice.Gpu)]
    public void The_ai_device_round_trips(AiDevice device)
    {
        var viewModel = new AdvancedSettingsViewModel();
        viewModel.Load(new ApplicationSettings { AiDevice = device });

        var saved = new ApplicationSettings();
        viewModel.Save(saved);

        Assert.Equal(device, saved.AiDevice);
    }

    [Fact]
    public void An_unknown_ai_device_loads_as_auto()
    {
        var viewModel = new AdvancedSettingsViewModel();

        viewModel.Load(new ApplicationSettings { AiDevice = (AiDevice)42 });

        Assert.Equal((int)AiDevice.Auto, viewModel.SelectedAiDeviceIndex);
    }

    [Fact]
    public void Choosing_a_device_marks_the_section_dirty()
    {
        var viewModel = new AdvancedSettingsViewModel();
        viewModel.Load(new ApplicationSettings());
        viewModel.MarkClean();

        viewModel.SelectedAiDeviceIndex = (int)AiDevice.Cpu;

        Assert.True(viewModel.IsDirty);
    }
}
