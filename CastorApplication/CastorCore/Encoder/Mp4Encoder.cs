using FFMpegCore;
using FFMpegCore.Pipes;

namespace CastorCore.Encoder
{
    public class Mp4Encoder
    {
        private readonly IPipeSource _videoPipe;
        private readonly IPipeSource _audioPipe;

        private readonly string _outputPath;
        private readonly double _frameRate;

        public Mp4Encoder(IPipeSource videoPipe, IPipeSource audioPipe, string outputPath, double frameRate = 60.0)
        {
            _videoPipe = videoPipe ?? throw new ArgumentNullException(nameof(videoPipe));
            _audioPipe = audioPipe ?? throw new ArgumentNullException(nameof(videoPipe));

            _outputPath = outputPath ?? throw new ArgumentNullException(nameof(outputPath));
            _frameRate = frameRate;
        }

        public async Task StartAsync(CancellationToken cts = default)
        {
            Console.WriteLine($"[Encoder] Starting FFmpeg with framerate: {_frameRate}");

            await FFMpegArguments
                .FromPipeInput(_audioPipe)
                .AddPipeInput(_videoPipe)
                .OutputToFile(_outputPath, overwrite: true, options => options
                    .WithVideoCodec("libx264")
                    .WithAudioCodec("aac")
                    .WithFramerate(_frameRate)
                    .ForceFormat("mp4")
                    .WithFastStart()
                )
                .ProcessAsynchronously();

            Console.WriteLine($"[Encoder] FFmpeg completed successfully");
        }
    }
}

