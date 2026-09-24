using Castor.IA.Proto;
using CastorApplication.Models.Config;
using CastorApplication.Models.Studio;
using CastorApplication.Services.Ai;
using CastorApplication.ViewModels.Multicam;
using CastorApplication.ViewModels.Scenes;
using CastorApplication.ViewModels.Studio;

namespace Castor.Studio.Tests;

public sealed class MulticamAiSessionTests
{
    [Theory]
    [InlineData("rtmp://127.0.0.1:1935/live/", "rtmp://127.0.0.1:1935/live/")]
    [InlineData("rtmp://example/live", "rtmp://example/live/")]
    public void Rtmp_urls_are_deterministic(string baseUrl, string expectedPrefix)
    {
        var sceneId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

        Assert.Equal($"{expectedPrefix}00112233445566778899aabbccddeeff",
            AiSceneStreamConfiguration.BuildPullUrl(baseUrl, sceneId));
    }

    [Fact]
    public async Task Starting_without_a_selected_scene_is_rejected()
    {
        var client = new FakeAiClient();
        var workspace = new StudioWorkspaceViewModel();
        workspace.CreateSceneWithVideo("Plateau");
        var viewModel = new MulticamViewModel(client, workspace);

        await viewModel.SetAiAgentCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsAiEnabled);
        Assert.Contains("Sélectionnez", viewModel.AiError);
        Assert.Empty(client.StartedModes);
    }

    [Fact]
    public async Task Agent_suggestion_waits_for_manual_application()
    {
        var client = new FakeAiClient();
        var workspace = new StudioWorkspaceViewModel();
        var first = workspace.CreateSceneWithVideo("Plateau");
        var second = workspace.CreateSceneWithVideo("Caméra");
        workspace.SelectScene(first);
        var viewModel = new MulticamViewModel(client, workspace);
        viewModel.Tiles.Single(tile => tile.Scene == second).IsSelected = true;

        await viewModel.SetAiAgentCommand.ExecuteAsync(null);
        client.Suggest(second.Id, 0.87f);

        Assert.Same(first, workspace.ActiveScene);
        Assert.Equal("Caméra", viewModel.AiSuggestionName);
        Assert.True(viewModel.HasAiSuggestion);

        viewModel.ApplyAiSuggestionCommand.Execute(null);

        Assert.Same(second, workspace.ActiveScene);
    }

    [Fact]
    public async Task Auto_mode_applies_a_known_suggestion()
    {
        var client = new FakeAiClient();
        var workspace = new StudioWorkspaceViewModel();
        var first = workspace.CreateSceneWithVideo("Plateau");
        var second = workspace.CreateSceneWithVideo("Caméra");
        workspace.SelectScene(first);
        var viewModel = new MulticamViewModel(client, workspace);
        viewModel.Tiles.Single(tile => tile.Scene == second).IsSelected = true;

        await viewModel.SetAiAutoCommand.ExecuteAsync(null);
        Assert.Equal("auto", Assert.Single(client.StartedModes));
        client.Suggest(second.Id, 0.91f);

        Assert.Same(second, workspace.ActiveScene);
    }

    [Fact]
    public async Task Mode_is_sent_to_the_client()
    {
        var client = new FakeAiClient();
        var workspace = new StudioWorkspaceViewModel();
        var scene = workspace.CreateSceneWithVideo("Plateau");
        var viewModel = new MulticamViewModel(client, workspace);
        viewModel.Tiles.Single(tile => tile.Scene == scene).IsSelected = true;

        await viewModel.SetAiAgentCommand.ExecuteAsync(null);

        Assert.Equal("agent", Assert.Single(client.StartedModes));
        Assert.Equal("football", client.ModuleName);
    }

    private sealed class FakeAiClient : IAiAnalysisClient
    {
        public bool IsAvailable => true;
        public string UnavailableMessage => "";
        public bool HasActiveSession { get; private set; }
        public string? SessionId { get; private set; }
        public IReadOnlySet<Guid> ActiveSceneIds { get; private set; } = new HashSet<Guid>();
        public List<string> StartedModes { get; } = [];
        public string? ModuleName { get; private set; }

        public event EventHandler<AiSceneSwitchEvent>? SceneSwitchSuggested;
        public event EventHandler<AiSessionStatusEvent>? SessionStatusChanged
        {
            add { }
            remove { }
        }
        public event EventHandler<AiServerErrorEvent>? ServerErrorReceived
        {
            add { }
            remove { }
        }

        public Task StartSessionAsync(string moduleName, string mode, IReadOnlyList<SceneDefinition> scenes,
            CancellationToken cancellationToken)
        {
            ModuleName = moduleName;
            StartedModes.Add(mode);
            HasActiveSession = true;
            SessionId = Guid.NewGuid().ToString("N");
            ActiveSceneIds = scenes.Select(scene => scene.Id).ToHashSet();
            return Task.CompletedTask;
        }

        public Task UpdateSourcesAsync(IReadOnlyList<SceneDefinition> scenes, CancellationToken cancellationToken)
        {
            ActiveSceneIds = scenes.Select(scene => scene.Id).ToHashSet();
            return Task.CompletedTask;
        }

        public Task StopSessionAsync(string reason, CancellationToken cancellationToken)
        {
            HasActiveSession = false;
            SessionId = null;
            ActiveSceneIds = new HashSet<Guid>();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Suggest(Guid sceneId, float confidence) =>
            SceneSwitchSuggested?.Invoke(this, new AiSceneSwitchEvent(sceneId.ToString("N"), confidence));
    }
}

internal static class WorkspaceTestExtensions
{
    public static SceneItemViewModel CreateSceneWithVideo(this StudioWorkspaceViewModel workspace, string name)
    {
        var scene = workspace.CreateScene(name);
        workspace.AddSource(scene, new SourceDefinition { Name = $"{name} source", Kind = SourceKind.Video });
        return scene;
    }
}
