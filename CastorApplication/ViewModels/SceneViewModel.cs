using System.Collections.ObjectModel;
using System.Linq;
using Castor.Native;
using CastorApplication.Models;
using CastorApplication.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.ViewModels;

public partial class ScenesViewModel : ViewModelBase
{
    // ── Scènes ────────────────────────────────────────────────────────────────

    public ObservableCollection<SceneItem> Scenes => SceneService.Instance.Scenes;

    [ObservableProperty]
    private SceneItem? _selectedScene;

    [ObservableProperty]
    private string _newSceneName = "";

    // ── Sources disponibles (pour le flyout d'ajout) ──────────────────────────

    public ObservableCollection<CaptureSourceOption> AvailableMonitors  { get; } = new();
    public ObservableCollection<CaptureSourceOption> AvailableCameras   { get; } = new();
    public ObservableCollection<CaptureSourceOption> AvailableWindows   { get; } = new();
    public ObservableCollection<AudioSourceOption>   AvailableLoopbacks { get; } = new();
    public ObservableCollection<AudioSourceOption>   AvailableMics      { get; } = new();

    // ── Constructeur ─────────────────────────────────────────────────────────

    public ScenesViewModel()
    {
        // Synchronise la sélection UI avec la scène active du service
        SelectedScene = SceneService.Instance.ActiveScene;

        // Charge les sources disponibles depuis la DLL
        LoadAvailableSources();
    }

    private void LoadAvailableSources()
    {
        try
        {
            foreach (var src in CastorNative.ListVideoSources())
            {
                var opt = new CaptureSourceOption(src);
                switch (src.Type)
                {
                    case CaptureSourceType.Monitor: AvailableMonitors.Add(opt);  break;
                    case CaptureSourceType.Camera:  AvailableCameras.Add(opt);   break;
                    case CaptureSourceType.Window:  AvailableWindows.Add(opt);   break;
                }
            }

            foreach (var src in CastorNative.ListAudioSources())
            {
                var opt = new AudioSourceOption(src);
                if (src.Type is AudioSourceType.Microphone or AudioSourceType.CameraMic)
                    AvailableMics.Add(opt);
                else
                    AvailableLoopbacks.Add(opt);
            }
        }
        catch { /* DLL non chargée en mode design */ }
    }

    // ── Commandes scènes ──────────────────────────────────────────────────────

    [RelayCommand]
    private void SelectScene(SceneItem scene)
    {
        SceneService.Instance.SetActiveScene(scene);
        SelectedScene = scene;
    }

    [RelayCommand]
    private void CreateScene()
    {
        if (string.IsNullOrWhiteSpace(NewSceneName)) return;

        var scene = SceneService.Instance.CreateScene(NewSceneName.Trim());
        NewSceneName = "";
        SelectScene(scene);
    }

    [RelayCommand]
    private void DeleteScene(SceneItem scene) => SceneService.Instance.DeleteScene(scene);

    // ── Commandes d'ajout de sources ─────────────────────────────────────────

    /// <summary>Ajoute une source vidéo spécifique à la scène sélectionnée.</summary>
    [RelayCommand]
    private void AddSpecificVideoSource(CaptureSourceOption opt)
    {
        if (SelectedScene == null) return;
        SceneService.Instance.AddVideoSource(SelectedScene, opt.Info);
    }

    /// <summary>Ajoute une source audio spécifique à la scène sélectionnée.</summary>
    [RelayCommand]
    private void AddSpecificAudioSource(AudioSourceOption opt)
    {
        if (SelectedScene == null) return;
        SceneService.Instance.AddAudioSource(SelectedScene, opt.Info);
    }

    // ── Suppression de source ─────────────────────────────────────────────────

    [RelayCommand]
    private void RemoveSource(SourceItem source)
    {
        if (SelectedScene == null) return;
        SceneService.Instance.RemoveSource(SelectedScene, source);
    }
}
