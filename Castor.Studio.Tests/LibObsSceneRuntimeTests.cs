using CastorApplication.Services.Studio;
using LibObs;

namespace Castor.Studio.Tests;

public sealed class LibObsSceneRuntimeTests
{
    [Fact]
    public void Native_scene_lifecycle_deduplicates_names_and_shuts_down_cleanly()
    {
        var runtime = new LibObsSceneRuntime();
        try
        {
            Assert.True(runtime.IsAvailable, runtime.UnavailableMessage);
            var requestedName = $"Castor test {Guid.NewGuid():N}";
            var firstId = Guid.NewGuid();
            var secondId = Guid.NewGuid();

            var first = runtime.CreateScene(firstId, requestedName);
            var second = runtime.CreateScene(secondId, requestedName);

            Assert.True(first.IsSuccess, first.Message);
            Assert.True(second.IsSuccess, second.Message);
            Assert.Equal(requestedName, first.EffectiveName);
            Assert.Equal($"{requestedName} 2", second.EffectiveName);

            var renamed = runtime.RenameScene(secondId, requestedName);
            Assert.True(renamed.IsSuccess, renamed.Message);
            Assert.Equal($"{requestedName} 2", renamed.EffectiveName);

            Assert.True(runtime.RemoveScene(firstId).IsSuccess);
            Assert.True(runtime.RemoveScene(secondId).IsSuccess);
        }
        finally
        {
            runtime.Dispose();
        }

        Assert.False(Obs.IsInitialized);
    }
}
