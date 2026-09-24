using CastorApplication.Models.Config;
using CastorApplication.Models.Studio;
using CastorApplication.Services.Ai;
using CastorApplication.Services.Config;

namespace Castor.Studio.Tests;

public sealed class AiSceneStreamRuntimeTests
{
    [Fact]
    public async Task Multiple_scene_outputs_have_independent_urls_and_stop_individually()
    {
        var backend = new FakeOutputRuntime();
        using var runtime = new LibObsAiSceneStreamRuntime(
            new FakeConfigService("rtmp://127.0.0.1:1935/live/"), backend);
        var first = new SceneDefinition { Id = Guid.NewGuid(), Name = "Plateau" };
        var second = new SceneDefinition { Id = Guid.NewGuid(), Name = "Caméra" };

        await runtime.StartStreamsAsync([first, second], CancellationToken.None);

        Assert.Equal(2, backend.Started.Count);
        Assert.Equal(AiSceneStreamConfiguration.BuildPullUrl(null, first.Id),
            runtime.ActiveStreams[first.Id].PullUrl);
        Assert.NotEqual(runtime.ActiveStreams[first.Id].PullUrl,
            runtime.ActiveStreams[second.Id].PullUrl);

        await runtime.StopStreamsAsync([first.Id], CancellationToken.None);

        Assert.DoesNotContain(first.Id, runtime.ActiveStreams.Keys);
        Assert.Contains(second.Id, runtime.ActiveStreams.Keys);
        Assert.Equal([first.Id], backend.Stopped);
    }

    [Fact]
    public async Task A_failed_second_output_rolls_back_the_first_output()
    {
        var first = new SceneDefinition { Id = Guid.NewGuid(), Name = "Plateau" };
        var second = new SceneDefinition { Id = Guid.NewGuid(), Name = "Caméra" };
        var backend = new FakeOutputRuntime { FailingSceneId = second.Id };
        using var runtime = new LibObsAiSceneStreamRuntime(new FakeConfigService("rtmp://ai/live"), backend);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.StartStreamsAsync([first, second], CancellationToken.None));

        Assert.Empty(runtime.ActiveStreams);
        Assert.Equal([first.Id], backend.Stopped);
    }

    private sealed class FakeConfigService(string baseUrl) : IConfigService
    {
        public AppConfig Config { get; } = new()
        {
            AiServer = new AiServerConfig { MediaMtxRtmpBaseUrl = baseUrl }
        };

        public ProviderConfig GetProviderConfig(string name) => throw new KeyNotFoundException(name);
    }

    private sealed class FakeOutputRuntime : IIndependentSceneOutputRuntime
    {
        public bool IsAvailable => true;
        public string UnavailableMessage => "";
        public Guid? FailingSceneId { get; init; }
        public List<(Guid SceneId, string Url)> Started { get; } = [];
        public List<Guid> Stopped { get; } = [];
        public event EventHandler<IndependentSceneOutputStateChangedEventArgs>? StateChanged
        {
            add { }
            remove { }
        }

        public Task StartAsync(SceneDefinition scene, string pushUrl, CancellationToken cancellationToken)
        {
            if (scene.Id == FailingSceneId)
                throw new InvalidOperationException("synthetic output failure");
            Started.Add((scene.Id, pushUrl));
            return Task.CompletedTask;
        }

        public Task StopAsync(Guid sceneId, CancellationToken cancellationToken)
        {
            Stopped.Add(sceneId);
            return Task.CompletedTask;
        }

        public void Dispose() { }
    }
}
