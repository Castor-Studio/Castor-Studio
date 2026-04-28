using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LibVLCSharp.Shared;
using LibVLCSharp.Avalonia;
using System;
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

        public StudioView()
        {
            InitializeComponent();

            try
            {
                LibVLCSharp.Shared.Core.Initialize();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur d'initialisation VLC : {ex.Message}");
            }

            _libVLC      = new LibVLC("--network-caching=150");
            _mediaPlayer = new MediaPlayer(_libVLC);

            // Retry automatique si le flux n'est pas encore disponible ou se coupe
            // ConfigureAwait(false) pour sortir du thread d'événement VLC avant de rappeler Play
            _mediaPlayer.EncounteredError += async (_, _) =>
            {
                await Task.Delay(2000).ConfigureAwait(false);
                PlayPreview();
            };

            _mediaPlayer.EndReached += async (_, _) =>
            {
                await Task.Delay(1000).ConfigureAwait(false);
                PlayPreview();
            };

            DataContextChanged += OnDataContextChanged;
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
            if (e.PropertyName != nameof(StudioViewModel.ActiveScene)) return;

            // La scène active a changé : démarre son preview si nécessaire, puis bascule VLC sur son URL.
            _ = Task.Run(async () =>
            {
                var scene = SceneService.Instance.ActiveScene;
                bool alreadyRunning = scene != null && RecorderService.Instance.IsPreviewActive(scene.Id);
                _vm?.EnsurePreviewRunning();
                // Délai uniquement si le recorder vient d'être créé (laisse le stream s'établir).
                // Si le preview tournait déjà, on bascule VLC immédiatement.
                if (!alreadyRunning)
                    await Task.Delay(1200).ConfigureAwait(false);
                PlayPreview();
            });
        }

        private void PlayPreview()
        {
            if (_libVLC == null || _mediaPlayer == null) return;

            var scene = SceneService.Instance.ActiveScene;
            if (scene == null) return;

            var url = MediaMtxService.GetPreviewPullUrl(scene.Id);
            var old = _currentMedia;
            _currentMedia = new Media(_libVLC, new Uri(url), ":network-caching=150");
            _mediaPlayer.Play(_currentMedia);
            old?.Dispose();
        }

        private void VideoView_OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            if (sender is VideoView videoView && _mediaPlayer != null)
            {
                if (videoView.MediaPlayer == null)
                    videoView.MediaPlayer = _mediaPlayer;

                // S'assure que libcastor pousse vers MediaMTX avant de lire
                if (DataContext is StudioViewModel vm)
                    vm.EnsurePreviewRunning();

                if (!_mediaPlayer.IsPlaying)
                    PlayPreview();
            }
        }

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
                // 1. On détache le lien avec le contrôle visuel (CRITIQUE)
                var videoView = this.FindControl<VideoView>("VideoPlayer");
                if (videoView != null)
                {
                    videoView.MediaPlayer = null;
                }

                // 2. On arrête le flux
                if (_mediaPlayer.IsPlaying)
                {
                    _mediaPlayer.Stop();
                }

                // 3. On libère dans l'ordre
                _currentMedia?.Dispose();
                _currentMedia = null;
                _mediaPlayer.Dispose();
                _libVLC?.Dispose();

                _mediaPlayer = null;
                _libVLC = null;
            }
        }
    }
}