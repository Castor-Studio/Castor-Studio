using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Castor.Engine.Models;
using Castor.Engine.Services;
using CastorApplication.Services.Ai;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.ViewModels;

public sealed partial class AiSceneSelection : ObservableObject
{
    public SceneItem Scene { get; }

    public string Name => Scene.Name;
    public int SourceCount => Scene.Sources.Count;

    [ObservableProperty]
    private bool _isSelected;

    public AiSceneSelection(SceneItem scene)
    {
        Scene = scene;
    }
}

public partial class MulticamViewModel : ViewModelBase
{
    private readonly IAiAnalysisClient _aiAnalysisClient;
    private readonly IStudioController _studioController;

    public ObservableCollection<SceneItem> Scenes => _studioController.Scenes;
    public ObservableCollection<AiSceneSelection> AiScenes { get; } = new();

    [ObservableProperty]
    private bool _isAiOff = true;

    [ObservableProperty]
    private bool _isAiAgent;

    [ObservableProperty]
    private bool _isAiAuto;

    [ObservableProperty]
    private int _selectedAiModelIndex;

    [ObservableProperty]
    private string _aiStatusText = "IA désactivée";

    [ObservableProperty]
    private string _aiError = "";

    [ObservableProperty]
    private bool _isAiBusy;

    public bool IsAiEnabled => !IsAiOff;

    public MulticamViewModel(IAiAnalysisClient aiAnalysisClient, IStudioController studioController)
    {
        _aiAnalysisClient = aiAnalysisClient;
        _studioController = studioController;

        RefreshAiScenes();
        _studioController.Scenes.CollectionChanged += (_, _) => RefreshAiScenes();

        _aiAnalysisClient.SceneSwitchSuggested += OnSceneSwitchSuggested;
        _aiAnalysisClient.SessionStatusChanged += OnSessionStatusChanged;
        _aiAnalysisClient.ServerErrorReceived += OnServerErrorReceived;
    }

    [RelayCommand]
    private void RefreshAiScenes()
    {
        var selectedIds = AiScenes
            .Where(item => item.IsSelected)
            .Select(item => item.Scene.Id)
            .ToHashSet();

        AiScenes.Clear();
        foreach (var scene in Scenes)
        {
            AiScenes.Add(new AiSceneSelection(scene)
            {
                IsSelected = selectedIds.Contains(scene.Id)
            });
        }
    }

    [RelayCommand]
    private async Task SetAiOff()
    {
        await StopAiAsync("user_disabled");
    }

    [RelayCommand]
    private async Task SetAiAgent()
    {
        await StartAiAsync("agent");
    }

    [RelayCommand]
    private async Task SetAiAuto()
    {
        await StartAiAsync("auto");
    }

    private async Task StartAiAsync(string mode)
    {
        if (IsAiBusy) return;

        IsAiBusy = true;
        AiError = "";
        AiStatusText = "Connexion IA...";

        var selectedScenes = AiScenes
            .Where(item => item.IsSelected)
            .Select(item => item.Scene)
            .ToList();

        if (selectedScenes.Count == 0)
        {
            AiError = "Sélectionnez au moins une scène.";
            AiStatusText = "IA désactivée";
            IsAiBusy = false;
            return;
        }

        try
        {
            await _aiAnalysisClient.StopSessionAsync("mode_switch", CancellationToken.None);

            var moduleConfig = new Dictionary<string, string>
            {
                ["mode"] = mode,
                ["module"] = GetSelectedModuleName()
            };

            await _aiAnalysisClient.StartSessionAsync(GetSelectedModuleName(), moduleConfig, CancellationToken.None);
            await _aiAnalysisClient.SendSourcesAsync(selectedScenes, CancellationToken.None);

            IsAiOff = false;
            IsAiAgent = mode == "agent";
            IsAiAuto = mode == "auto";
            AiStatusText = $"IA active - {selectedScenes.Count} scène(s)";
            OnPropertyChanged(nameof(IsAiEnabled));
        }
        catch (Exception ex)
        {
            await _aiAnalysisClient.StopSessionAsync("start_failed", CancellationToken.None);
            IsAiOff = true;
            IsAiAgent = false;
            IsAiAuto = false;
            AiStatusText = "IA indisponible";
            AiError = ex.Message;
            OnPropertyChanged(nameof(IsAiEnabled));
        }
        finally
        {
            IsAiBusy = false;
        }
    }

    private async Task StopAiAsync(string reason)
    {
        if (IsAiBusy) return;

        IsAiBusy = true;
        AiError = "";

        try
        {
            await _aiAnalysisClient.StopSessionAsync(reason, CancellationToken.None);
        }
        catch (Exception ex)
        {
            AiError = ex.Message;
        }
        finally
        {
            IsAiOff = true;
            IsAiAgent = false;
            IsAiAuto = false;
            AiStatusText = "IA désactivée";
            IsAiBusy = false;
            OnPropertyChanged(nameof(IsAiEnabled));
        }
    }

    private string GetSelectedModuleName() => SelectedAiModelIndex switch
    {
        1 => "podcast",
        _ => "football"
    };

    private void OnSceneSwitchSuggested(AiSceneSwitchEvent aiEvent)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var scene = Scenes.FirstOrDefault(item =>
                item.Id.ToString("N").Equals(aiEvent.SceneId, StringComparison.OrdinalIgnoreCase) ||
                item.Id.ToString().Equals(aiEvent.SceneId, StringComparison.OrdinalIgnoreCase));

            if (scene == null)
            {
                AiError = $"Scène IA inconnue: {aiEvent.SceneId}";
                return;
            }

            if (!IsAiAuto)
            {
                AiStatusText = $"Suggestion IA: {scene.Name} ({aiEvent.Confidence:P0})";
                return;
            }

            _studioController.SelectScene(scene);
            AiStatusText = $"Switch IA: {scene.Name} ({aiEvent.Confidence:P0})";
        });
    }

    private void OnSessionStatusChanged(AiSessionStatusEvent aiEvent)
    {
        Dispatcher.UIThread.Post(() =>
        {
            AiStatusText = string.IsNullOrWhiteSpace(aiEvent.Message)
                ? $"IA: {aiEvent.State}"
                : aiEvent.Message;
        });
    }

    private void OnServerErrorReceived(AiServerErrorEvent aiEvent)
    {
        Dispatcher.UIThread.Post(() =>
        {
            AiError = string.IsNullOrWhiteSpace(aiEvent.ErrorCode)
                ? aiEvent.ErrorMessage
                : $"{aiEvent.ErrorCode}: {aiEvent.ErrorMessage}";

            if (!aiEvent.IsFatal) return;

            IsAiOff = true;
            IsAiAgent = false;
            IsAiAuto = false;
            AiStatusText = "Erreur IA fatale";
            OnPropertyChanged(nameof(IsAiEnabled));
            _ = _aiAnalysisClient.StopSessionAsync("fatal_server_error", CancellationToken.None);
        });
    }
}
