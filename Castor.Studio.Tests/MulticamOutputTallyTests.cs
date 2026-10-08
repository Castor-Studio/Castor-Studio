using CastorApplication.Services.Ai;
using CastorApplication.ViewModels.Multicam;
using CastorApplication.ViewModels.Studio;

namespace Castor.Studio.Tests;

public sealed class MulticamOutputTallyTests
{
    private static MulticamViewModel CreateViewModel(StudioWorkspaceViewModel workspace) =>
        new(new UnavailableAiAnalysisClient(), workspace);

    [Theory]
    [InlineData(false, false, false, "Prête")]
    [InlineData(true, false, true, "En direct")]
    [InlineData(false, true, true, "REC")]
    [InlineData(true, true, true, "En direct + REC")]
    public void The_output_tile_says_whether_its_scene_actually_leaves_the_app(
        bool streaming, bool recording, bool live, string label)
    {
        var workspace = new StudioWorkspaceViewModel();
        var viewModel = CreateViewModel(workspace);
        var output = workspace.CreateScene("Plateau");
        workspace.CreateScene("Caméra");

        workspace.SetStreamingState(streaming);
        workspace.SetRecordingState(recording);

        var tile = viewModel.Tiles.Single(t => t.Scene == output);
        Assert.Equal(live, tile.IsOutputLive);
        Assert.Equal(label, tile.OutputLabel);
        Assert.Equal(live, viewModel.IsOutputLive);
        Assert.Equal(label, viewModel.OutputLabel);
    }

    [Fact]
    public void Only_the_output_scene_carries_the_tally()
    {
        var workspace = new StudioWorkspaceViewModel();
        var viewModel = CreateViewModel(workspace);
        workspace.CreateScene("Plateau");
        var other = workspace.CreateScene("Caméra");

        workspace.SetStreamingState(true);

        var tile = viewModel.Tiles.Single(t => t.Scene == other);
        Assert.False(tile.IsOutputLive);
        Assert.Equal("", tile.OutputLabel);
    }

    [Fact]
    public void Starting_a_stream_tells_the_output_tile_to_redraw()
    {
        var workspace = new StudioWorkspaceViewModel();
        var viewModel = CreateViewModel(workspace);
        workspace.CreateScene("Plateau");
        var tile = viewModel.Tiles.Single();
        var changed = new List<string?>();
        tile.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        workspace.SetStreamingState(true);

        Assert.Contains(nameof(MulticamSceneTile.IsOutputLive), changed);
        Assert.Contains(nameof(MulticamSceneTile.OutputLabel), changed);
    }
}
