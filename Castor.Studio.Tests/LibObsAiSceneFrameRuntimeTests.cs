using CastorApplication.Services.Ai;
using CastorApplication.Services.Studio;

namespace Castor.Studio.Tests;

[Collection(LibObsCollection.Name)]
public sealed class LibObsAiSceneFrameRuntimeTests(LibObsRuntimeFixture fixture)
{
    [Fact]
    public async Task Scenes_are_rendered_raw_at_the_requested_size()
    {
        var runtime = fixture.Runtime;
        Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
        var sceneId = Guid.NewGuid();
        Assert.True(runtime.CreateScene(sceneId, "AI frames").IsSuccess);
        Assert.True(runtime.AddSource(sceneId,
            new SourceAddRequest.Media(Guid.NewGuid(), "AI frames source", fixture.MediaPath, true)).IsSuccess);

        using var frames = new LibObsAiSceneFrameRuntime(runtime);
        var received = new TaskCompletionSource<(Guid Scene, int Width, int Height, int Stride, int Length)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            frames.Start([sceneId], 640, 360, (scene, bgra, width, height, stride) =>
                received.TrySetResult((scene, width, height, stride, bgra.Length)));

            var frame = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(sceneId, frame.Scene);
            Assert.Equal((640, 360), (frame.Width, frame.Height));
            Assert.True(frame.Stride >= 640 * 4);
            Assert.True(frame.Length >= frame.Stride * 359 + 640 * 4);
        }
        finally
        {
            frames.StopAll();
            runtime.RemoveScene(sceneId);
        }
    }

    [Fact]
    public void An_unknown_scene_is_refused()
    {
        using var frames = new LibObsAiSceneFrameRuntime(fixture.Runtime);

        var error = Assert.Throws<InvalidOperationException>(() =>
            frames.Start([Guid.NewGuid()], 640, 360, (_, _, _, _, _) => { }));

        Assert.Contains("n'existe pas", error.Message);
    }
}
