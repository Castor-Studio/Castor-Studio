using CastorApplication.Services.Ai;

namespace Castor.Studio.Tests;

public sealed class UnavailableRuntimeTests
{
    [Fact]
    public void Ai_client_is_explicitly_unavailable()
    {
        var client = new UnavailableAiAnalysisClient("Worker IA introuvable");

        Assert.False(client.IsAvailable);
        Assert.Equal("Worker IA introuvable", client.UnavailableMessage);
    }
}
