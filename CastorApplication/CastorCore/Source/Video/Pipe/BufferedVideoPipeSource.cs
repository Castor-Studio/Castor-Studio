using FFMpegCore.Pipes;

namespace CastorCore.Source.Video.Pipe
{
    /// <summary>
    /// A custom pipe source that buffers frames before FFmpeg reads them,
    /// avoiding the "Enumerator is empty" error from RawVideoPipeSource.
    /// </summary>
    public class BufferedVideoPipeSource : IPipeSource
    {
        private readonly IEnumerable<IVideoFrame> _framesSource;
        private readonly int _width;
        private readonly int _height;
        private readonly double _frameRate;

        public string StreamFormat => "bgra";

        public BufferedVideoPipeSource(IEnumerable<IVideoFrame> framesSource, int width, int height, double frameRate)
        {
            _framesSource = framesSource ?? throw new ArgumentNullException(nameof(framesSource));
            _width = width;
            _height = height;
            _frameRate = frameRate;
        }

        public string GetStreamArguments()
        {
            // Return FFmpeg arguments for raw video input
            return $"-f rawvideo -pix_fmt bgra -video_size {_width}x{_height} -framerate {_frameRate}";
        }

        public async Task WriteAsync(Stream outputStream, CancellationToken cancellationToken)
        {
            int frameCount = 0;
            var startTime = DateTime.Now;

            try
            {
                Console.WriteLine($"[PIPE] WriteAsync started. Waiting for frames...");

                foreach (var frame in _framesSource)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Console.WriteLine($"\n[PIPE] Cancellation requested after {frameCount} frames");
                        break;
                    }

                    try
                    {
                        await frame.SerializeAsync(outputStream, cancellationToken);
                        frameCount++;

                        if (frameCount == 1)
                        {
                            Console.WriteLine($"[PIPE] First frame sent to FFmpeg!");
                        }
                        else if (frameCount % 60 == 0)
                        {
                            var elapsed = (DateTime.Now - startTime).TotalSeconds;
                            var actualFps = frameCount / elapsed;
                            Console.WriteLine($"[PIPE] {frameCount} frames sent | Actual FPS: {actualFps:F2}");
                        }
                    }
                    finally
                    {
                        // Dispose frame after serialization
                        if (frame is IDisposable disposable)
                        {
                            disposable.Dispose();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[PIPE] Error after {frameCount} frames: {ex.GetType().Name} - {ex.Message}");
                throw;
            }
            finally
            {
                var totalTime = (DateTime.Now - startTime).TotalSeconds;
                Console.WriteLine($"\n[PIPE] Completed. Total: {frameCount} frames in {totalTime:F2}s (Avg FPS: {frameCount / totalTime:F2})");
            }
        }
    }
}
