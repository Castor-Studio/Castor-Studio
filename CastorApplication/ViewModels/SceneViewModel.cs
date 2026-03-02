using System.Collections.ObjectModel;
using CastorApplication.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.ViewModels;

public partial class ScenesViewModel : ViewModelBase
{
    public ObservableCollection<SceneItem> Scenes { get; } = new();

    [ObservableProperty]
    private SceneItem? _selectedScene;

    [ObservableProperty]
    private string _newSceneName = "";

    public ScenesViewModel()
    {
        // Sample data
        var matchLive = new SceneItem("Match Live", isActive: true, isLive: true);
        matchLive.Sources.Add(new SourceItem("Caméra terrain", "Vidéo", "#3498db"));
        matchLive.Sources.Add(new SourceItem("Tableau de scores", "Vidéo", "#22c55e"));
        matchLive.Sources.Add(new SourceItem("Audio système", "Audio", "#f59e0b"));

        var intro = new SceneItem("Introduction");
        intro.Sources.Add(new SourceItem("Logo club", "Vidéo", "#3498db"));
        intro.Sources.Add(new SourceItem("Musique intro", "Audio", "#f59e0b"));

        var miTemps = new SceneItem("Mi-Temps");
        miTemps.Sources.Add(new SourceItem("Caméra plateau", "Vidéo", "#22c55e"));

        var finMatch = new SceneItem("Fin de Match");
        var plateau = new SceneItem("Plateau Studio");

        Scenes.Add(matchLive);
        Scenes.Add(intro);
        Scenes.Add(miTemps);
        Scenes.Add(finMatch);
        Scenes.Add(plateau);

        SelectedScene = matchLive;
    }

    [RelayCommand]
    private void SelectScene(SceneItem scene)
    {
        // Deselect all
        foreach (var s in Scenes)
            s.IsActive = false;

        scene.IsActive = true;
        SelectedScene = scene;
    }

    [RelayCommand]
    private void CreateScene()
    {
        if (string.IsNullOrWhiteSpace(NewSceneName))
            return;

        var scene = new SceneItem(NewSceneName.Trim());
        Scenes.Add(scene);
        NewSceneName = "";

        SelectScene(scene);
    }

    [RelayCommand]
    private void DeleteScene(SceneItem scene)
    {
        if (Scenes.Count <= 1)
            return;

        Scenes.Remove(scene);

        if (SelectedScene == scene && Scenes.Count > 0)
            SelectScene(Scenes[0]);
    }

    [RelayCommand]
    private void AddVideoSource()
    {
        SelectedScene?.Sources.Add(new SourceItem("Nouvelle source vidéo", "Vidéo", "#3498db"));
    }

    [RelayCommand]
    private void AddScreenCapture()
    {
        SelectedScene?.Sources.Add(new SourceItem("Capture d'écran", "Vidéo", "#3498db"));
    }

    [RelayCommand]
    private void AddCamera()
    {
        SelectedScene?.Sources.Add(new SourceItem("Caméra", "Vidéo", "#22c55e"));
    }

    [RelayCommand]
    private void AddWindowCapture()
    {
        SelectedScene?.Sources.Add(new SourceItem("Capture de fenêtre", "Vidéo", "#9b59b6"));
    }

    [RelayCommand]
    private void AddSystemAudio()
    {
        SelectedScene?.Sources.Add(new SourceItem("Audio système", "Audio", "#f59e0b"));
    }

    [RelayCommand]
    private void AddMicrophone()
    {
        SelectedScene?.Sources.Add(new SourceItem("Microphone", "Audio", "#e74c3c"));
    }

    [RelayCommand]
    private void RemoveSource(SourceItem source)
    {
        SelectedScene?.Sources.Remove(source);
    }
}
