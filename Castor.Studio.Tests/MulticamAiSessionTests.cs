using Castor.AI.Providers;
using CastorApplication.Models.Studio;
using CastorApplication.Services.Ai;
using CastorApplication.ViewModels.Multicam;
using CastorApplication.ViewModels.Scenes;
using CastorApplication.ViewModels.Studio;

namespace Castor.Studio.Tests;

public sealed class MulticamAiSessionTests
{
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
        Assert.Equal($"{0.87f:P0}", viewModel.AiSuggestionConfidence);
        Assert.True(viewModel.HasAiSuggestion);

        viewModel.ApplyAiSuggestionCommand.Execute(null);

        Assert.Same(second, workspace.ActiveScene);
        // The applied suggestion is a cut the AI must hear about.
        Assert.Equal([second.Id], client.ReportedOnAir);
    }

    [Fact]
    public async Task Auto_mode_switches_to_the_scene_the_ai_took()
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
        client.Request(second.Id, 0.91f);

        Assert.Same(second, workspace.ActiveScene);
        Assert.Contains("Caméra", viewModel.AiStatusText);
    }

    [Fact]
    public async Task Auto_mode_ignores_agent_suggestions()
    {
        var client = new FakeAiClient();
        var workspace = new StudioWorkspaceViewModel();
        var first = workspace.CreateSceneWithVideo("Plateau");
        var second = workspace.CreateSceneWithVideo("Caméra");
        workspace.SelectScene(first);
        var viewModel = new MulticamViewModel(client, workspace);
        viewModel.Tiles.Single(tile => tile.Scene == second).IsSelected = true;

        await viewModel.SetAiAutoCommand.ExecuteAsync(null);
        client.Suggest(second.Id, 0.91f);

        Assert.Same(first, workspace.ActiveScene);
        Assert.False(viewModel.HasAiSuggestion);
    }

    [Fact]
    public async Task Mode_module_and_scene_on_air_are_sent_to_the_client()
    {
        var client = new FakeAiClient();
        var workspace = new StudioWorkspaceViewModel();
        var scene = workspace.CreateSceneWithVideo("Plateau");
        workspace.SelectScene(scene);
        var viewModel = new MulticamViewModel(client, workspace);
        viewModel.Tiles.Single(tile => tile.Scene == scene).IsSelected = true;

        await viewModel.SetAiAgentCommand.ExecuteAsync(null);

        Assert.Equal("agent", Assert.Single(client.StartedModes));
        Assert.Equal("football", client.ModuleName);
        Assert.Equal(scene.Id, client.OnAirAtStart);
    }

    [Fact]
    public async Task Operator_cuts_are_reported_while_the_ai_runs()
    {
        var client = new FakeAiClient();
        var workspace = new StudioWorkspaceViewModel();
        var first = workspace.CreateSceneWithVideo("Plateau");
        var second = workspace.CreateSceneWithVideo("Caméra");
        var viewModel = new MulticamViewModel(client, workspace);

        workspace.SelectScene(second);
        Assert.Empty(client.ReportedOnAir);

        viewModel.Tiles.Single(tile => tile.Scene == first).IsSelected = true;
        await viewModel.SetAiAgentCommand.ExecuteAsync(null);
        workspace.SelectScene(first);

        Assert.Equal([first.Id], client.ReportedOnAir);
    }

    [Fact]
    public async Task Ticking_another_scene_restarts_the_session_with_it()
    {
        var client = new FakeAiClient();
        var workspace = new StudioWorkspaceViewModel();
        var first = workspace.CreateSceneWithVideo("Plateau");
        var second = workspace.CreateSceneWithVideo("Caméra");
        var viewModel = new MulticamViewModel(client, workspace);
        viewModel.Tiles.Single(tile => tile.Scene == first).IsSelected = true;
        await viewModel.SetAiAgentCommand.ExecuteAsync(null);

        viewModel.Tiles.Single(tile => tile.Scene == second).IsSelected = true;

        Assert.Equal(new HashSet<Guid> { first.Id, second.Id }, Assert.Single(client.Updates));
    }

    [Fact]
    public async Task Model_download_progress_is_shown()
    {
        var client = new FakeAiClient();
        var workspace = new StudioWorkspaceViewModel();
        var scene = workspace.CreateSceneWithVideo("Plateau");
        var viewModel = new MulticamViewModel(client, workspace);
        viewModel.Tiles.Single(tile => tile.Scene == scene).IsSelected = true;
        client.OnStart = () => client.Status(AiConnectionState.Starting, "downloading football/checkpoint_5_416.onnx 42 %");
        string? shown = null;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MulticamViewModel.AiStatusText) && viewModel.AiStatusText.Contains("42 %"))
                shown = viewModel.AiStatusText;
        };

        await viewModel.SetAiAgentCommand.ExecuteAsync(null);

        Assert.Equal("Démarrage de l'IA : downloading football/checkpoint_5_416.onnx 42 %", shown);
    }

    [Fact]
    public async Task A_worker_restart_is_shown_and_clears_once_running()
    {
        var client = new FakeAiClient();
        var workspace = new StudioWorkspaceViewModel();
        var scene = workspace.CreateSceneWithVideo("Plateau");
        var viewModel = new MulticamViewModel(client, workspace);
        viewModel.Tiles.Single(tile => tile.Scene == scene).IsSelected = true;
        await viewModel.SetAiAutoCommand.ExecuteAsync(null);

        client.Error("Le worker IA s'est arrêté : exit -1", fatal: false);
        client.Status(AiConnectionState.Faulted, "exit -1");
        client.Status(AiConnectionState.Restarting, "Restarting in 1 s (attempt 1)");

        Assert.True(viewModel.IsAiEnabled);
        Assert.Equal("Le worker IA s'est arrêté : exit -1", viewModel.AiError);
        Assert.StartsWith("Le worker IA redémarre", viewModel.AiStatusText);

        client.Status(AiConnectionState.Running, "running");

        Assert.Equal("", viewModel.AiError);
        Assert.Equal(MulticamAiSessionState.Active, viewModel.AiSessionState);
    }

    [Fact]
    public async Task A_start_failure_is_shown()
    {
        var client = new FakeAiClient { StartFailure = "Worker IA introuvable." };
        var workspace = new StudioWorkspaceViewModel();
        var scene = workspace.CreateSceneWithVideo("Plateau");
        var viewModel = new MulticamViewModel(client, workspace);
        viewModel.Tiles.Single(tile => tile.Scene == scene).IsSelected = true;

        await viewModel.SetAiAgentCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsAiEnabled);
        Assert.Equal(MulticamAiSessionState.Error, viewModel.AiSessionState);
        Assert.Equal("Worker IA introuvable.", viewModel.AiError);
    }

    [Fact]
    public void Every_audio_source_of_the_workspace_can_be_a_microphone()
    {
        var workspace = new StudioWorkspaceViewModel();
        var viewModel = new MulticamViewModel(new FakeAiClient(), workspace);
        var host = workspace.CreateSceneWithVideo("Animateur");
        var mic = workspace.AddSource(host, new SourceDefinition { Name = "Micro 1", Kind = SourceKind.Audio });

        Assert.Equal(["Aucun micro", "Micro 1 (Animateur)"], viewModel.AiMicrophoneOptions.Select(o => o.Label));
        Assert.Same(viewModel.AiMicrophoneOptions, viewModel.Tiles.Single().AiMicrophoneOptions);

        var tile = viewModel.Tiles.Single();
        tile.AiMicrophone = viewModel.AiMicrophoneOptions[1];
        host.Sources.Remove(mic);

        Assert.Equal(["Aucun micro"], viewModel.AiMicrophoneOptions.Select(o => o.Label));
        Assert.True(tile.AiMicrophone.IsNone);
    }

    [Fact]
    public async Task Podcast_scenes_send_their_role_and_microphone()
    {
        var client = new FakeAiClient();
        var workspace = new StudioWorkspaceViewModel();
        var host = workspace.CreateSceneWithVideo("Animateur");
        var mic = workspace.AddSource(host, new SourceDefinition { Name = "Micro 1", Kind = SourceKind.Audio });
        var viewModel = new MulticamViewModel(client, workspace) { SelectedAiModelIndex = 1 };
        var tile = viewModel.Tiles.Single();
        tile.IsSelected = true;
        tile.AiRole = tile.AiRoleOptions.Single(role => role.Value == "host");
        tile.AiMicrophone = viewModel.AiMicrophoneOptions.Single(option => option.SourceId == mic.Id);

        Assert.True(tile.ShowAiRole);
        await viewModel.SetAiAgentCommand.ExecuteAsync(null);

        Assert.Equal("podcast", client.ModuleName);
        var setup = Assert.Single(client.Setups);
        Assert.Equal("host", setup.Role);
        Assert.Equal(new AiMicrophone(host.Id, mic.Id), setup.Microphone);
    }

    [Fact]
    public async Task Football_sends_no_role()
    {
        var client = new FakeAiClient();
        var workspace = new StudioWorkspaceViewModel();
        var scene = workspace.CreateSceneWithVideo("Plateau");
        var viewModel = new MulticamViewModel(client, workspace);
        var tile = viewModel.Tiles.Single();
        tile.IsSelected = true;
        tile.AiRole = tile.AiRoleOptions[1];

        Assert.False(tile.ShowAiRole);
        Assert.True(tile.ShowAiMicrophone);
        await viewModel.SetAiAutoCommand.ExecuteAsync(null);

        Assert.Equal("", Assert.Single(client.Setups).Role);
    }

    [Fact]
    public async Task Changing_a_microphone_restarts_the_running_session()
    {
        var client = new FakeAiClient();
        var workspace = new StudioWorkspaceViewModel();
        var host = workspace.CreateSceneWithVideo("Animateur");
        workspace.AddSource(host, new SourceDefinition { Name = "Micro 1", Kind = SourceKind.Audio });
        var viewModel = new MulticamViewModel(client, workspace) { SelectedAiModelIndex = 1 };
        var tile = viewModel.Tiles.Single();
        tile.IsSelected = true;
        await viewModel.SetAiAgentCommand.ExecuteAsync(null);

        tile.AiMicrophone = viewModel.AiMicrophoneOptions[1];

        Assert.Single(client.Updates);
        Assert.NotNull(Assert.Single(client.Setups).Microphone);
    }

    private sealed class FakeAiClient : IAiAnalysisClient
    {
        public bool IsAvailable => true;
        public string UnavailableMessage => "";
        public bool HasActiveSession { get; private set; }
        public IReadOnlySet<Guid> ActiveSceneIds { get; private set; } = new HashSet<Guid>();
        public List<string> StartedModes { get; } = [];
        public string? ModuleName { get; private set; }
        public Guid? OnAirAtStart { get; private set; }
        public List<Guid> ReportedOnAir { get; } = [];
        public List<HashSet<Guid>> Updates { get; } = [];
        public IReadOnlyList<AiSceneSetup> Setups { get; private set; } = [];
        public string? StartFailure { get; init; }
        public Action? OnStart { get; set; }

        public event EventHandler<AiSceneSwitchEvent>? SceneSwitchSuggested;
        public event EventHandler<AiSceneSwitchEvent>? SceneSwitchRequested;
        public event EventHandler<AiSessionStatusEvent>? SessionStatusChanged;
        public event EventHandler<AiServerErrorEvent>? ServerErrorReceived;

        public Task StartSessionAsync(string moduleName, string mode, IReadOnlyList<AiSceneSetup> scenes,
            Guid? onAirSceneId, CancellationToken cancellationToken)
        {
            OnStart?.Invoke();
            if (StartFailure != null) return Task.FromException(new InvalidOperationException(StartFailure));
            ModuleName = moduleName;
            StartedModes.Add(mode);
            OnAirAtStart = onAirSceneId;
            HasActiveSession = true;
            Setups = scenes;
            ActiveSceneIds = scenes.Select(setup => setup.Scene.Id).ToHashSet();
            return Task.CompletedTask;
        }

        public Task UpdateSourcesAsync(IReadOnlyList<AiSceneSetup> scenes, Guid? onAirSceneId,
            CancellationToken cancellationToken)
        {
            Setups = scenes;
            ActiveSceneIds = scenes.Select(setup => setup.Scene.Id).ToHashSet();
            Updates.Add(ActiveSceneIds.ToHashSet());
            return Task.CompletedTask;
        }

        public Task StopSessionAsync(string reason, CancellationToken cancellationToken)
        {
            HasActiveSession = false;
            ActiveSceneIds = new HashSet<Guid>();
            return Task.CompletedTask;
        }

        public void ReportOnAir(Guid sceneId) => ReportedOnAir.Add(sceneId);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Suggest(Guid sceneId, float confidence) =>
            SceneSwitchSuggested?.Invoke(this, new AiSceneSwitchEvent(sceneId, confidence));

        public void Request(Guid sceneId, float confidence) =>
            SceneSwitchRequested?.Invoke(this, new AiSceneSwitchEvent(sceneId, confidence));

        public void Status(AiConnectionState state, string message) =>
            SessionStatusChanged?.Invoke(this, new AiSessionStatusEvent(state, message));

        public void Error(string message, bool fatal) =>
            ServerErrorReceived?.Invoke(this, new AiServerErrorEvent("TEST", message, fatal));
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
