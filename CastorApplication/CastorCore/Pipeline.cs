using CastorCore.Encoder;
using CastorCore.Source.Audio;
using CastorCore.Source.Video;
using FFMpegCore.Pipes;

namespace CastorCore
{
    public sealed class Pipeline
    {
        private IVideoSource _videoSource;
        private IAudioSource _audioSource;

        private IPipeSource _videoPipe;
        private IPipeSource _audioPipe;

        private Mp4Encoder _encoder;

        public Pipeline(IVideoSource videoSource, IAudioSource audioSource)
        {
            _videoSource = videoSource ?? throw new ArgumentNullException(nameof(videoSource));
            _audioSource = audioSource ?? throw new ArgumentNullException(nameof(videoSource));

            _videoPipe = _videoSource.ToPipeSource();
            _audioPipe = _audioSource.ToPipeSource();

            string outputPath = $"output_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";

            _encoder = new Mp4Encoder(_videoPipe, _audioPipe, outputPath);
        }

        public async Task StartAsync(CancellationToken cts = default)
        {   
            Console.WriteLine("[Pipeline] Starting video source...");
            _videoSource.StartCapture();
            Console.WriteLine("[Pipeline] Starting audio source...");
            _audioSource.StartCapture();

            Console.WriteLine("[Pipeline] Starting encoder...");
            await _encoder.StartAsync(cts);
        }

        public async Task StopAsync()
        {
            Console.WriteLine("[Pipeline] Stopping video source...");
            _videoSource.StopCapture();
            Console.WriteLine("[Pipeline] Stopping audio source...");
            _audioSource.StopCapture();
        }
    }
}
