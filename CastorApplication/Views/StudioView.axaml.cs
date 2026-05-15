using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LibVLCSharp.Shared;
using LibVLCSharp.Avalonia;
using System;
using System.IO;
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
                // Use the shared LibVLC instance so we don't have two parallel
                // VLC engines fighting over the audio device on macOS.
                _libVLC = VlcService.Instance.LibVLC;
                if (_libVLC != null)
                {
                    _mediaPlayer = new MediaPlayer(_libVLC);
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
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur d'initialisation VLC Studio : {ex.Message}");
                _libVLC = null;
                _mediaPlayer = null;
            }

            // The Multicam page launches the AI; here we just mirror its
            // "active source" choice into the Studio video preview.
            PodcastAiService.Instance.ActiveSourceChanged += OnAiActiveSourceChanged;
            PodcastAiService.Instance.RunningChanged     += OnAiRunningChanged;

            DataContextChanged += OnDataContextChanged;
        }

        private void OnAiActiveSourceChanged(int _)
        {
            // In fake (demo) mode the pre-rendered output is already playing —
            // ignore per-source switches so we don't restart playback every tick.
            if (PodcastAiService.Instance.IsFakeMode) return;
            var uri = PodcastAiService.Instance.CurrentSourceUri;
            System.Diagnostics.Debug.WriteLine($"[Studio] AI switch → {uri}");
            if (string.IsNullOrEmpty(uri)) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => PlayUri(uri));
        }

        private void OnAiRunningChanged(bool running)
        {
            System.Diagnostics.Debug.WriteLine($"[Studio] AI running={running}");
            if (!running) return;
            var ai  = PodcastAiService.Instance;
            var uri = ai.IsFakeMode ? ai.FakeOutputUri : ai.CurrentSourceUri;
            if (string.IsNullOrEmpty(uri)) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => PlayUri(uri!));
        }

        private void PlayUri(string uri)
        {
            if (_libVLC == null || _mediaPlayer == null) return;
            try
            {
                Media media;
                if (File.Exists(uri))
                    media = new Media(_libVLC, new Uri(uri, UriKind.Absolute), ":input-repeat=65535");
                else
                    media = new Media(_libVLC, new Uri(uri), ":network-caching=150");
                var old = _currentMedia;
                _currentMedia = media;
                _mediaPlayer.Play(media);
                old?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Studio] PlayUri failed: {ex.Message}");
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

                // If the AI is already running (Multicam tab clicked Auto first),
                // mirror its current source. Otherwise fall back to the scene
                // preview pipeline (Windows-only path with libcastor + MediaMTX).
                var ai    = PodcastAiService.Instance;
                var aiUri = ai.IsFakeMode ? ai.FakeOutputUri : ai.CurrentSourceUri;
                if (!string.IsNullOrEmpty(aiUri))
                {
                    PlayUri(aiUri!);
                    return;
                }

                if (OperatingSystem.IsWindows())
                {
                    if (DataContext is StudioViewModel vm)
                        vm.EnsurePreviewRunning();
                    if (!_mediaPlayer.IsPlaying)
                        PlayPreview();
                }
            }
        }

        protected override void OnUnloaded(Avalonia.Interactivity.RoutedEventArgs e)
        {
            base.OnUnloaded(e);

            PodcastAiService.Instance.ActiveSourceChanged -= OnAiActiveSourceChanged;
            PodcastAiService.Instance.RunningChanged     -= OnAiRunningChanged;

            if (_vm != null)
            {
                _vm.PropertyChanged -= OnVmPropertyChanged;
                _vm = null;
            }

            if (_mediaPlayer != null)
            {
                // 1. Detach from the VideoView control.
                var videoView = this.FindControl<VideoView>("VideoPlayer");
                if (videoView != null)
                {
                    videoView.MediaPlayer = null;
                }

                // 2. Stop the stream.
                if (_mediaPlayer.IsPlaying)
                    _mediaPlayer.Stop();

                // 3. Free MediaPlayer + current media. Do NOT dispose the
                //    shared LibVLC — it's owned by VlcService and used by
                //    other views (the multicam previews).
                _currentMedia?.Dispose();
                _currentMedia = null;
                _mediaPlayer.Dispose();
                _mediaPlayer = null;
                _libVLC = null;
            }
        }
    }
}