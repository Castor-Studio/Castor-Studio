using System;
using System.Diagnostics;
using System.Threading;
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
    private ScenesViewModel? _vm;

    private volatile bool _isDisposed;
    private readonly object _playLock = new();
    private CancellationTokenSource _playCts = new();

    public ScenesView()
    {
        InitializeComponent();

        try { LibVLCSharp.Shared.Core.Initialize(); }
        catch (Exception ex) { Debug.WriteLine($"[ScenesPreview] Erreur init VLC : {ex.Message}"); }

        _libVLC = new LibVLC(
            "--network-caching=50",
            "--live-caching=50",
            "--clock-jitter=0",
            "--clock-synchro=0"
        );
        _mediaPlayer = new MediaPlayer(_libVLC);

        _mediaPlayer.EncounteredError += async (_, _) =>
        {
            if (_isDisposed) return;
            var cts = _playCts;
            try { await Task.Delay(2000, cts.Token).ConfigureAwait(false); }
            catch (Exception) { return; }
            if (_isDisposed) return;
            var scene = _vm?.SelectedScene;
            if (scene != null) EnsureScenePreview(scene);
            PlayScenePreview(_vm?.SelectedScene);
        };

        _mediaPlayer.EndReached += async (_, _) =>
        {
            if (_isDisposed) return;
            var cts = _playCts;
            try { await Task.Delay(1000, cts.Token).ConfigureAwait(false); }
            catch (Exception) { return; }
            if (_isDisposed) return;
            PlayScenePreview(_vm?.SelectedScene);
        };

        DataContextChanged += OnDataContextChanged;
    }

    // ── Réaction au changement de DataContext ─────────────────────────────────

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        _vm = DataContext as ScenesViewModel;

        if (_vm != null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;

            if (_mediaPlayer != null)
                _mediaPlayer.Volume = (int)_vm.PlayerVolume;

            if (_vm.SelectedScene != null)
                EnsureScenePreview(_vm.SelectedScene);
        }
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScenesViewModel.PlayerVolume))
        {
            if (_mediaPlayer != null && _vm != null)
                _mediaPlayer.Volume = (int)_vm.PlayerVolume;
            return;
        }

        if (e.PropertyName != nameof(ScenesViewModel.SelectedScene)) return;
        if (_vm?.SelectedScene == null) return;

        var newCts = new CancellationTokenSource();
        var oldCts = Interlocked.Exchange(ref _playCts, newCts);
        oldCts.Cancel();
        oldCts.Dispose();

        var scene = _vm.SelectedScene;
        _ = Task.Run(() =>
        {
            EnsureScenePreview(scene);
            if (!newCts.IsCancellationRequested)
                PlayScenePreview(scene);
        });
    }

    // ── Gestion du preview ────────────────────────────────────────────────────

    private void EnsureScenePreview(SceneItem scene)
    {
        if (RecorderService.Instance.IsPreviewActive(scene.Id))
        {
            PlayScenePreview(scene);
            return;
        }

        _ = Task.Run(async () =>
        {
            int r = RecorderService.Instance.StartPreview(scene);
            Debug.WriteLine($"[ScenesPreview] StartPreview '{scene.Name}' : {r}");
            if (r == 0)
            {
                await Task.Delay(600).ConfigureAwait(false);
                PlayScenePreview(scene);
            }
        });
    }

    private void PlayScenePreview(SceneItem? scene)
    {
        if (_isDisposed || scene == null || _libVLC == null || _mediaPlayer == null) return;

        var url = MediaMtxService.GetPreviewPullUrl(scene.Id);

        Media? old;
        lock (_playLock)
        {
            if (_isDisposed) return;
            old           = _currentMedia;
            _currentMedia = new Media(_libVLC, new Uri(url), ":network-caching=150");
            _mediaPlayer.Play(_currentMedia);
        }
        old?.Dispose();
    }

    // ── Attachement du VideoView ──────────────────────────────────────────────

    private void SceneVideoView_OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is VideoView videoView && _mediaPlayer != null)
        {
            if (videoView.MediaPlayer == null)
                videoView.MediaPlayer = _mediaPlayer;

            if (_vm != null)
                _mediaPlayer.Volume = (int)_vm.PlayerVolume;

            var scene = _vm?.SelectedScene;
            if (scene != null && !_mediaPlayer.IsPlaying)
                EnsureScenePreview(scene);
        }
    }

    // ── Nettoyage ─────────────────────────────────────────────────────────────

    protected override void OnUnloaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        _isDisposed = true;
        _playCts.Cancel();
        _playCts.Dispose();

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

            lock (_playLock)
            {
                _currentMedia?.Dispose();
                _currentMedia = null;
            }

            _mediaPlayer.Dispose();
            _mediaPlayer = null;

            _libVLC?.Dispose();
            _libVLC = null;
        }
    }
}
