using CastorApplication.Services.Ai;
using CastorApplication.Services.Studio;
using LibObs;

namespace Castor.Studio.Tests;

[Collection(LibObsCollection.Name)]
public sealed class LibObsAiAudioCaptureRuntimeTests(LibObsRuntimeFixture fixture)
{
    [Fact]
    public async Task A_source_is_heard_alone_in_mono_on_its_channel()
    {
        var runtime = fixture.Runtime;
        Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
        var sceneId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        Assert.True(runtime.CreateScene(sceneId, "AI audio").IsSuccess);
        Assert.True(runtime.AddSource(sceneId,
            new SourceAddRequest.Media(sourceId, "AI audio source", fixture.MediaPath, true)).IsSuccess);

        using var audio = new LibObsAiAudioCaptureRuntime(runtime);
        var received = new TaskCompletionSource<(int Channel, int Samples, int SampleRate)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            audio.Start(new Dictionary<AiMicrophone, int> { [new AiMicrophone(sceneId, sourceId)] = 2 },
                (channel, mono, sampleRate) => received.TrySetResult((channel, mono.Length, sampleRate)));

            var block = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(2, block.Channel);
            Assert.True(block.Samples > 0);
            Assert.Equal((int)Obs.AudioInfo!.SamplesPerSecond, block.SampleRate);
        }
        finally
        {
            audio.StopAll();
            runtime.RemoveScene(sceneId);
        }
    }

    [Fact]
    public void A_missing_microphone_is_named_and_the_others_still_start()
    {
        using var audio = new LibObsAiAudioCaptureRuntime(fixture.Runtime);

        var error = Assert.Throws<InvalidOperationException>(() => audio.Start(
            new Dictionary<AiMicrophone, int> { [new AiMicrophone(Guid.NewGuid(), Guid.NewGuid())] = 0 },
            (_, _, _) => { }));

        Assert.Contains("n'existe plus", error.Message);
    }

    [Theory]
    [InlineData(30u, 1u, 15.0, 2u)]
    [InlineData(60u, 1u, 15.0, 4u)]
    [InlineData(30000u, 1001u, 15.0, 1u)]
    [InlineData(25u, 1u, 15.0, 1u)]
    [InlineData(60u, 1u, 0.0, 1u)]
    public void Frames_the_ai_would_drop_are_not_delivered(uint numerator, uint denominator, double fps, uint expected)
    {
        var info = Obs.VideoInfo! with { FpsNumerator = numerator, FpsDenominator = denominator };

        Assert.Equal(expected, LibObsAiSceneFrameRuntime.FrameRateDivisor(info, fps));
    }
}
