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
        matchLive.Sources.Add(new SourceItem("Caméra terrain", "Vidéo", "#5b8def"));
        matchLive.Sources.Add(new SourceItem("Tableau de scores", "Vidéo", "#34d399"));
        matchLive.Sources.Add(new SourceItem("Audio système", "Audio", "#fbbf24"));

        var intro = new SceneItem("Introduction");
        intro.Sources.Add(new SourceItem("Logo club", "Vidéo", "#5b8def"));
        intro.Sources.Add(new SourceItem("Musique intro", "Audio", "#fbbf24"));

        var miTemps = new SceneItem("Mi-Temps");
        miTemps.Sources.Add(new SourceItem("Caméra plateau", "Vidéo", "#34d399"));

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
        SelectedScene?.Sources.Add(new SourceItem("Nouvelle source vidéo", "Vidéo", "#5b8def"));
    }

    [RelayCommand]
    private void AddScreenCapture()
    {
        SelectedScene?.Sources.Add(new SourceItem("Capture d'écran", "Vidéo", "#5b8def"));
    }

    [RelayCommand]
    private void AddCamera()
    {
        SelectedScene?.Sources.Add(new SourceItem("Caméra", "Vidéo", "#34d399"));
    }

    [RelayCommand]
    private void AddWindowCapture()
    {
        SelectedScene?.Sources.Add(new SourceItem("Capture de fenêtre", "Vidéo", "#8888a0"));
    }

    [RelayCommand]
    private void AddSystemAudio()
    {
        SelectedScene?.Sources.Add(new SourceItem("Audio système", "Audio", "#fbbf24"));
    }

    [RelayCommand]
    private void AddMicrophone()
    {
        SelectedScene?.Sources.Add(new SourceItem("Microphone", "Audio", "#f87171"));
    }

    [RelayCommand]
    private void RemoveSource(SourceItem source)
    {
        SelectedScene?.Sources.Remove(source);
    }
}
