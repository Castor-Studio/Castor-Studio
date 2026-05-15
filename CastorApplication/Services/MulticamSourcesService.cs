using System.Collections.ObjectModel;
using Avalonia.Threading;
using CastorApplication.Models;

namespace CastorApplication.Services;

/// <summary>
/// Application-wide store for the multicam source list. Singleton so the
/// camera selection survives tab/view recreation (Dock layout disposes
/// MulticamViewModel when the user navigates away). Also owns the
/// AI-driven IsActive/IsLive sync so it doesn't depend on a live VM.
/// </summary>
public sealed class MulticamSourcesService
{
    private static MulticamSourcesService? _instance;
    public static MulticamSourcesService Instance => _instance ??= new MulticamSourcesService();

    public ObservableCollection<CameraItem> Cameras { get; } = new();

    /// <summary>Optional path to a pre-rendered AI output video used in
    /// "demo mode" (canned output played in Studio).</summary>
    public string? FakeOutputPath { get; set; }

    private MulticamSourcesService()
    {
        // Mirror the AI's "active source" decision into the camera list.
        // Wired once at app startup so ViewModel lifetimes don't matter.
        PodcastAiService.Instance.ActiveSourceChanged += OnAiActiveSourceChanged;
    }

    private void OnAiActiveSourceChanged(int index)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (index < 0 || index >= Cameras.Count) return;
            for (int i = 0; i < Cameras.Count; i++)
            {
                var on = i == index;
                Cameras[i].IsActive = on;
                Cameras[i].IsLive   = on;
            }
        });
    }
}
