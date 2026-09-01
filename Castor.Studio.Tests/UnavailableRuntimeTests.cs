using CastorApplication.Models.Studio;
using CastorApplication.Services.Ai;
using CastorApplication.Services.Studio;

namespace Castor.Studio.Tests;

public sealed class UnavailableRuntimeTests
{
    [Fact]
    public async Task Runtime_never_reports_success_or_devices()
    {
        var runtime = new UnavailableStudioRuntime();

        var devices = await runtime.GetVideoSourcesAsync(CancellationToken.None);
        var result = await runtime.StartPreviewAsync(new SceneDefinition(), CancellationToken.None);

        Assert.Empty(devices);
        Assert.False(runtime.IsAvailable);
        Assert.Equal(StudioRuntimeStatus.Unavailable, result.Status);
        Assert.Contains("LibObs", result.Message);
    }

    [Fact]
    public void Ai_client_is_explicitly_unavailable()
    {
        var client = new UnavailableAiAnalysisClient();

        Assert.False(client.IsAvailable);
        Assert.Contains("LibObs", client.UnavailableMessage);
    }
}
