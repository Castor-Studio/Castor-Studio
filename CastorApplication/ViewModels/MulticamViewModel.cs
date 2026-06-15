using System.Collections.ObjectModel;
using CastorApplication.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.ViewModels;

public partial class MulticamViewModel : ViewModelBase
{
    // ── Camera list ──

    public ObservableCollection<CameraItem> Cameras { get; } = new();

    // ── AI Mode (F7/F8) ──

    [ObservableProperty]
    private bool _isAiOff = true;

    [ObservableProperty]
    private bool _isAiAgent;

    [ObservableProperty]
    private bool _isAiAuto;

    [ObservableProperty]
    private int _selectedAiModelIndex;

    public bool IsAiEnabled => !IsAiOff;

    // ── Constructor ──

    public MulticamViewModel()
    {
        Cameras.Add(new CameraItem("CAM 1", "Caméra Principale", isActive: true, isLive: true));
        Cameras.Add(new CameraItem("CAM 2", "Caméra Terrain"));
        Cameras.Add(new CameraItem("CAM 3", "Vue Tribunes"));
    }

    // ── Camera commands ──

    [RelayCommand]
    private void SelectCamera(CameraItem camera)
    {
        foreach (var cam in Cameras)
        {
            cam.IsActive = false;
            cam.IsLive = false;
        }

        camera.IsActive = true;
        camera.IsLive = true;
    }

    [RelayCommand]
    private void AddCamera()
    {
        var index = Cameras.Count + 1;
        Cameras.Add(new CameraItem($"CAM {index}", $"Caméra {index}"));
    }

    // ── AI Mode commands ──

    [RelayCommand]
    private void SetAiOff()
    {
        IsAiOff = true;
        IsAiAgent = false;
        IsAiAuto = false;
        OnPropertyChanged(nameof(IsAiEnabled));
    }

    [RelayCommand]
    private void SetAiAgent()
    {
        IsAiOff = false;
        IsAiAgent = true;
        IsAiAuto = false;
        OnPropertyChanged(nameof(IsAiEnabled));
    }

    [RelayCommand]
    private void SetAiAuto()
    {
        IsAiOff = false;
        IsAiAgent = false;
        IsAiAuto = true;
        OnPropertyChanged(nameof(IsAiEnabled));
    }
}
