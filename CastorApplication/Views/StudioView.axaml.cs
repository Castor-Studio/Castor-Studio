using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LibVLCSharp.Shared;
using LibVLCSharp.Avalonia;
using System;
using System.Threading;
using System.Threading.Tasks;
using CastorApplication.Services;
using CastorApplication.ViewModels;

namespace CastorApplication.Views
{
    public partial class StudioView : UserControl
    {
        private LibVLC? _libVLC;
        private MediaPlayer? _mediaPlayer;
        private Media? _currentMedia;
        private StudioViewModel? _vm;

        // Flag pour éviter tout appel natif VLC après libération des ressources
        private volatile bool _isDisposed;

        // ── Thread-safety de PlayPreview ─────────────────────────────────────────
        // Sérialise les swaps de Media pour éviter double-dispose et appels Play
        // concurrents depuis les callbacks VLC, les Task.Run de switching et l'UI thread.
        private readonly object _playLock = new();

        // Permet d'annuler un PlayPreview différé quand un deuxième switch arrive
        // avant que le délai d'attente soit écoulé.
        private CancellationTokenSource _playCts = new();

        public StudioView()
        {
            InitializeComponent();

            DataContextChanged += OnDataContextChanged;

            try
            {
                LibVLCSharp.Shared.Core.Initialize();

                _libVLC      = new LibVLC(
                    "--network-caching=50",   // réduit de 150 → 50 ms
                    "--live-caching=50",       // cache dédié aux flux RTMP live
                    "--clock-jitter=0",        // désactive la compensation de jitter (ajoute du délai)
                    "--clock-synchro=0"        // désactive la re-sync PTS (source de buffering supplémentaire)
                );
                _mediaPlayer = new MediaPlayer(_libVLC);

                // Retry automatique si le flux n'est pas encore disponible ou se coupe.
                // On reappelle EnsurePreviewRunning() pour relancer le recorder si
                // le démarrage initial a échoué (MediaMTX pas encore prêt, capture lente…).
                _mediaPlayer.EncounteredError += async (_, _) =>
                {
                    if (_isDisposed) return;
                    var cts = _playCts;
                    try { await Task.Delay(2000, cts.Token).ConfigureAwait(false); }
                    catch (Exception) { return; }   // OperationCanceledException ou ObjectDisposedException
                    if (_isDisposed) return;
                    _vm?.EnsurePreviewRunning();    // repart le recorder s'il avait échoué
                    PlayPreview();
                };

                _mediaPlayer.EndReached += async (_, _) =>
                {
                    if (_isDisposed) return;
                    var cts = _playCts;
                    try { await Task.Delay(1000, cts.Token).ConfigureAwait(false); }
                    catch (Exception) { return; }
                    if (_isDisposed) return;
                    _vm?.EnsurePreviewRunning();
                    PlayPreview();
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur d'initialisation VLC : {ex.Message}");
            }
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_vm != null)
                _vm.PropertyChanged -= OnVmPropertyChanged;

            _vm = DataContext as StudioViewModel;

            if (_vm != null)
                _vm.PropertyChanged += OnVmPropertyChanged;
        }

        private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Volume du player : appliqué immédiatement à VLC (0–100)
            if (e.PropertyName == nameof(StudioViewModel.PlayerVolume))
            {
                if (_mediaPlayer != null && _vm != null)
                    _mediaPlayer.Volume = (int)_vm.PlayerVolume;
                return;
            }

            if (e.PropertyName != nameof(StudioViewModel.ActiveScene)) return;

            // Scène active changée :
            //   1. Annule tout PlayPreview différé en cours (switch précédent pas encore joué).
            //   2. Lance le preview de la nouvelle scène si nécessaire.
            //   3. Bascule VLC immédiatement → si le flux n'est pas prêt, le retry prend le relais.
            var newCts = new CancellationTokenSource();
            var oldCts = Interlocked.Exchange(ref _playCts, newCts);
            oldCts.Cancel();
            oldCts.Dispose();

            _ = Task.Run(() =>
            {
                _vm?.EnsurePreviewRunning();
                // On joue immédiatement : le retry EncounteredError gère le cas où
                // le flux MediaMTX n'est pas encore prêt, sans créer de fenêtre de désync.
                if (!newCts.IsCancellationRequested)
                    PlayPreview();
            });
        }

        /// <summary>
        /// Swaps atomiquement le Media VLC vers l'URL de la scène active.
        /// Thread-safe : peut être appelé depuis n'importe quel thread.
        /// </summary>
        private void PlayPreview()
        {
            if (_isDisposed || _libVLC == null || _mediaPlayer == null) return;

            var scene = SceneService.Instance.ActiveScene;
            if (scene == null) return;

            var url = MediaMtxService.GetPreviewPullUrl(scene.Id);

            Media? old;
            lock (_playLock)
            {
                if (_isDisposed) return;   // re-check sous le verrou
                old           = _currentMedia;
                _currentMedia = new Media(_libVLC, new Uri(url), ":network-caching=150");
                _mediaPlayer.Play(_currentMedia);
            }
            // Dispose hors du verrou pour ne pas bloquer d'autres appels entrants
            old?.Dispose();
        }

        private void VideoView_OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            if (sender is VideoView videoView && _mediaPlayer != null)
            {
                if (videoView.MediaPlayer == null)
                    videoView.MediaPlayer = _mediaPlayer;

                if (DataContext is StudioViewModel vm)
                {
                    _mediaPlayer.Volume = (int)vm.PlayerVolume;
                    vm.EnsurePreviewRunning();
                }

                if (!_mediaPlayer.IsPlaying)
                    PlayPreview();
            }
        }

        protected override void OnUnloaded(Avalonia.Interactivity.RoutedEventArgs e)
        {
            base.OnUnloaded(e);

            // 1. Bloque tout nouveau PlayPreview venant des callbacks VLC en vol.
            _isDisposed = true;

            // 2. Annule les retries/delays en attente.
            _playCts.Cancel();
            _playCts.Dispose();

            if (_vm != null)
            {
                _vm.PropertyChanged -= OnVmPropertyChanged;
                _vm = null;
            }

            if (_mediaPlayer != null)
            {
                var videoView = this.FindControl<VideoView>("VideoPlayer");
                if (videoView != null)
                    videoView.MediaPlayer = null;

                if (_mediaPlayer.IsPlaying)
                    _mediaPlayer.Stop();

                // Acquiert le verrou pour s'assurer qu'aucun PlayPreview n'est en cours
                // avant de libérer les ressources.
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
}