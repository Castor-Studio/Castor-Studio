using CastorApplication.Services.Ai;
using CastorApplication.ViewModels.Multicam;
using CastorApplication.ViewModels.Studio;

namespace Castor.Studio.Tests;

public sealed class SharedWorkspaceTests
{
    [Fact]
    public void Multicam_observes_scenes_added_to_the_shared_workspace()
    {
        var workspace = new StudioWorkspaceViewModel();
        var multicam = new MulticamViewModel(new UnavailableAiAnalysisClient(), workspace);

        var scene = workspace.CreateScene("Caméra principale");

        var selection = Assert.Single(multicam.AiScenes);
        Assert.Same(scene, selection.Scene);
        Assert.Equal("Caméra principale", selection.Name);
    }
}
