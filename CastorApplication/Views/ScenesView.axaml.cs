using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using LibVLCSharp.Avalonia;
using LibVLCSharp.Shared;
using CastorApplication.Models;
using CastorApplication.Services;
using CastorApplication.ViewModels;

namespace CastorApplication.Views;

public partial class ScenesView : UserControl
{
    private LibVLC? _libVLC;
    private MediaPlayer? _mediaPlayer;
    private Media? _currentMedia;

    // Garde une référence sur le ViewModel pour se désabonner proprement
    private ScenesViewModel? _vm;

    public ScenesView()
    {
        InitializeComponent();

        try { LibVLCSharp.Shared.Core.Initialize(); }
        catch (Exception ex) { Debug.WriteLine($"[ScenesPreview] Erreur init VLC : {ex.Message}"); }

        _libVLC      = new LibVLC("--network-caching=150");
        _mediaPlayer = new MediaPlayer(_libVLC);

        // Retry automatique si le flux n'est pas encore dispo ou se coupe
        _mediaPlayer.EncounteredError += async (_, _) =>
        {
            await Task.Delay(2000).ConfigureAwait(false);
            PlayScenePreview(GetSelectedScene());
        };

        _mediaPlayer.EndReached += async (_, _) =>
        {
            await Task.Delay(1000).ConfigureAwait(false);
            PlayScenePreview(GetSelectedScene());
        };

        DataContextChanged += OnDataContextChanged;
    }

    // ── Réaction au changement de DataContext ─────────────────────────────────

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Désabonnement de l'ancien VM
        if (_vm != null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        _vm = DataContext as ScenesViewModel;

        if (_vm != null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;

            // Lance le preview de la scène déjà sélectionnée (si applicable)
            if (_vm.SelectedScene != null)
                EnsureScenePreview(_vm.SelectedScene);
        }
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScenesViewModel.SelectedScene) && _vm?.SelectedScene != null)
            EnsureScenePreview(_vm.SelectedScene);
    }

    // ── Gestion du preview ────────────────────────────────────────────────────

    private void EnsureScenePreview(SceneItem scene)
    {
        if (RecorderService.Instance.IsPreviewActive(scene.Id))
        {
            // Le flux tourne déjà — on branche directement VLC dessus
            PlayScenePreview(scene);
            return;
        }

        // Pas encore actif — démarre en background (le sémaphore dans RecorderService
        // garantit qu'un seul Start peut s'exécuter à la fois, même si plusieurs
        // threads arrivent ici simultanément).
        _ = Task.Run(async () =>
        {
            int r = RecorderService.Instance.StartPreview(scene);
            Debug.WriteLine($"[ScenesPreview] StartPreview '{scene.Name}' : {r}");
            if (r == 0)
            {
                await Task.Delay(600).ConfigureAwait(false); // laisse le flux s'établir
                PlayScenePreview(scene);
            }
        });
    }

    private void PlayScenePreview(SceneItem? scene)
    {
        if (scene == null || _libVLC == null || _mediaPlayer == null) return;

        var url = MediaMtxService.GetPreviewPullUrl(scene.Id);
        var old = _currentMedia;
        _currentMedia = new Media(_libVLC, new Uri(url), ":network-caching=150");
        _mediaPlayer.Play(_currentMedia);
        old?.Dispose();
    }

    private SceneItem? GetSelectedScene() => _vm?.SelectedScene;

    // ── Attachement du VideoView ──────────────────────────────────────────────

    private void SceneVideoView_OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is VideoView videoView && _mediaPlayer != null)
        {
            if (videoView.MediaPlayer == null)
                videoView.MediaPlayer = _mediaPlayer;

            // Si une scène est déjà sélectionnée, lance son preview
            var scene = GetSelectedScene();
            if (scene != null && !_mediaPlayer.IsPlaying)
                EnsureScenePreview(scene);
        }
    }

    // ── Nettoyage ─────────────────────────────────────────────────────────────

    protected override void OnUnloaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        if (_vm != null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm = null;
        }

        if (_mediaPlayer != null)
        {
            var videoView = this.FindControl<VideoView>("ScenePreviewPlayer");
            if (videoView != null)
                videoView.MediaPlayer = null;

            if (_mediaPlayer.IsPlaying)
                _mediaPlayer.Stop();

            _currentMedia?.Dispose();
            _currentMedia = null;
            _mediaPlayer.Dispose();
            _libVLC?.Dispose();

            _mediaPlayer = null;
            _libVLC = null;
        }
    }
}
