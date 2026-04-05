using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LibVLCSharp.Shared;
using LibVLCSharp.Avalonia;
using System;

namespace CastorApplication.Views
{
    public partial class StudioView : UserControl
    {
        private LibVLC? _libVLC;
        private MediaPlayer? _mediaPlayer;

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

            // On initialise le moteur et le player ici
            _libVLC = new LibVLC("--network-caching=400");
            _mediaPlayer = new MediaPlayer(_libVLC);
        }

        // Cette méthode doit être SEULE, en dehors du constructeur
        private void VideoView_OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            if (sender is VideoView videoView && _mediaPlayer != null)
            {
                // On ne branche le player que s'il n'est pas déjà branché
                if (videoView.MediaPlayer == null)
                {
                    videoView.MediaPlayer = _mediaPlayer;
                }

                if (!_mediaPlayer.IsPlaying)
                {
                    var media = new Media(_libVLC!, new Uri("rtmp://127.0.0.1/live/test"), ":network-caching=400");
                    _mediaPlayer.Play(media);
                }
            }
        }

        protected override void OnUnloaded(Avalonia.Interactivity.RoutedEventArgs e)
        {
            base.OnUnloaded(e);

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
                _mediaPlayer.Dispose();
                _libVLC?.Dispose();

                _mediaPlayer = null;
                _libVLC = null;
            }
        }
    }
}