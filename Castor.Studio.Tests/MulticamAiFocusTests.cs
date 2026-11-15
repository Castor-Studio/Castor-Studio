using CastorApplication.Services.Ai;
using CastorApplication.ViewModels.Multicam;
using CastorApplication.ViewModels.Studio;

namespace Castor.Studio.Tests;

public sealed class MulticamAiFocusTests
{
    private static MulticamViewModel CreateViewModel(StudioWorkspaceViewModel workspace) =>
        new(new UnavailableAiAnalysisClient(), workspace);

    [Fact]
    public void Tiles_start_with_no_ai_marking()
    {
        var workspace = new StudioWorkspaceViewModel();
        var viewModel = CreateViewModel(workspace);
        workspace.CreateScene("Plateau");

        var tile = Assert.Single(viewModel.Tiles);
        Assert.Equal(MulticamAiState.None, tile.AiState);
        Assert.False(tile.IsAiSelected);
        Assert.False(tile.IsAiConsidered);
    }

    [Fact]
    public void Setting_the_focus_marks_the_pick_and_its_candidates()
    {
        var workspace = new StudioWorkspaceViewModel();
        var viewModel = CreateViewModel(workspace);
        var wide = workspace.CreateScene("Plateau");
        var closeup = workspace.CreateScene("Caméra");
        var spare = workspace.CreateScene("Public");

        viewModel.SetAiFocus(closeup, [wide]);

        Assert.Equal(MulticamAiState.Selected, viewModel.Tiles.Single(t => t.Scene == closeup).AiState);
        Assert.Equal(MulticamAiState.Considered, viewModel.Tiles.Single(t => t.Scene == wide).AiState);
        Assert.Equal(MulticamAiState.None, viewModel.Tiles.Single(t => t.Scene == spare).AiState);
    }

    [Fact]
    public void A_new_focus_replaces_the_previous_one_entirely()
    {
        var workspace = new StudioWorkspaceViewModel();
        var viewModel = CreateViewModel(workspace);
        var wide = workspace.CreateScene("Plateau");
        var closeup = workspace.CreateScene("Caméra");

        viewModel.SetAiFocus(wide, [closeup]);
        viewModel.SetAiFocus(closeup);

        // The grid must never keep showing a decision the pipeline moved on from.
        Assert.Equal(MulticamAiState.None, viewModel.Tiles.Single(t => t.Scene == wide).AiState);
        Assert.Equal(MulticamAiState.Selected, viewModel.Tiles.Single(t => t.Scene == closeup).AiState);
    }

    [Fact]
    public void Clearing_the_focus_drops_every_marking()
    {
        var workspace = new StudioWorkspaceViewModel();
        var viewModel = CreateViewModel(workspace);
        var wide = workspace.CreateScene("Plateau");
        var closeup = workspace.CreateScene("Caméra");
        viewModel.SetAiFocus(wide, [closeup]);

        viewModel.SetAiFocus(null);

        Assert.All(viewModel.Tiles, tile => Assert.Equal(MulticamAiState.None, tile.AiState));
    }

    [Fact]
    public void The_marking_survives_a_scene_being_added()
    {
        var workspace = new StudioWorkspaceViewModel();
        var viewModel = CreateViewModel(workspace);
        var wide = workspace.CreateScene("Plateau");
        viewModel.SetAiFocus(wide);

        workspace.CreateScene("Caméra");

        Assert.Equal(MulticamAiState.Selected, viewModel.Tiles.Single(t => t.Scene == wide).AiState);
    }

    [Fact]
    public void A_scene_that_leaves_the_workspace_stops_being_the_pick()
    {
        var workspace = new StudioWorkspaceViewModel();
        var viewModel = CreateViewModel(workspace);
        var wide = workspace.CreateScene("Plateau");
        var closeup = workspace.CreateScene("Caméra");
        viewModel.SetAiFocus(wide, [closeup]);

        workspace.DeleteScene(wide);

        Assert.Equal(MulticamAiState.Considered, viewModel.Tiles.Single(t => t.Scene == closeup).AiState);
        Assert.DoesNotContain(viewModel.Tiles, tile => tile.IsAiSelected);
    }

    [Fact]
    public void The_ai_marking_is_independent_of_what_is_on_air()
    {
        var workspace = new StudioWorkspaceViewModel();
        var viewModel = CreateViewModel(workspace);
        var onAir = workspace.CreateScene("Plateau");
        var wanted = workspace.CreateScene("Caméra");
        workspace.SelectScene(onAir);

        viewModel.SetAiFocus(wanted);

        // The gap between what is broadcast and what the AI wants is the thing an
        // operator needs to see, so the two markings must not collapse into one.
        var onAirTile = viewModel.Tiles.Single(t => t.Scene == onAir);
        var wantedTile = viewModel.Tiles.Single(t => t.Scene == wanted);
        Assert.True(onAirTile.IsOnAir);
        Assert.False(onAirTile.IsAiSelected);
        Assert.False(wantedTile.IsOnAir);
        Assert.True(wantedTile.IsAiSelected);
    }
    [Fact]
    public void An_existing_tile_survives_another_scene_being_added()
    {
        var workspace = new StudioWorkspaceViewModel();
        var viewModel = CreateViewModel(workspace);
        workspace.CreateScene("Plateau");
        var firstTile = viewModel.Tiles.Single();
        firstTile.IsSelected = true;

        workspace.CreateScene("Caméra");

        // Rebuilding the collection here would restart every native preview on the
        // page, and lose whatever the operator had set on the tile.
        Assert.Same(firstTile, viewModel.Tiles[0]);
        Assert.True(viewModel.Tiles[0].IsSelected);
        Assert.Equal(2, viewModel.Tiles.Count);
    }

    [Fact]
    public void Tiles_follow_the_order_of_the_scenes()
    {
        var workspace = new StudioWorkspaceViewModel();
        var viewModel = CreateViewModel(workspace);
        var first = workspace.CreateScene("Plateau");
        var second = workspace.CreateScene("Caméra");
        var firstTile = viewModel.Tiles[0];

        workspace.Scenes.Move(0, 1);

        Assert.Equal([second, first], viewModel.Tiles.Select(tile => tile.Scene));
        // Moved, not rebuilt: the preview behind it keeps running.
        Assert.Same(firstTile, viewModel.Tiles[1]);
    }

    [Fact]
    public void A_removed_scene_drops_its_tile_and_leaves_the_others()
    {
        var workspace = new StudioWorkspaceViewModel();
        var viewModel = CreateViewModel(workspace);
        var first = workspace.CreateScene("Plateau");
        workspace.CreateScene("Caméra");
        var secondTile = viewModel.Tiles[1];

        workspace.DeleteScene(first);

        Assert.Same(secondTile, Assert.Single(viewModel.Tiles));
    }

    [Fact]
    public void Moving_a_scene_reorders_the_workspace_itself()
    {
        var workspace = new StudioWorkspaceViewModel();
        var viewModel = CreateViewModel(workspace);
        var first = workspace.CreateScene("Plateau");
        var second = workspace.CreateScene("Caméra");
        var third = workspace.CreateScene("Public");

        viewModel.MoveScene(third, first);

        // The workspace collection is what moves, so the order holds on every page
        // rather than only on this one.
        Assert.Equal([third, first, second], workspace.Scenes);
        Assert.Equal([third, first, second], viewModel.Tiles.Select(tile => tile.Scene));
    }

    [Fact]
    public void Moving_a_scene_keeps_the_tiles_alive()
    {
        var workspace = new StudioWorkspaceViewModel();
        var viewModel = CreateViewModel(workspace);
        var first = workspace.CreateScene("Plateau");
        var second = workspace.CreateScene("Caméra");
        var firstTile = viewModel.Tiles[0];
        var secondTile = viewModel.Tiles[1];

        viewModel.MoveScene(second, first);

        // Reordering must not restart the previews behind the tiles.
        Assert.Same(secondTile, viewModel.Tiles[0]);
        Assert.Same(firstTile, viewModel.Tiles[1]);
    }

    [Fact]
    public void Dropping_a_scene_on_itself_changes_nothing()
    {
        var workspace = new StudioWorkspaceViewModel();
        var viewModel = CreateViewModel(workspace);
        var first = workspace.CreateScene("Plateau");
        var second = workspace.CreateScene("Caméra");

        viewModel.MoveScene(first, first);

        Assert.Equal([first, second], workspace.Scenes);
    }

}
