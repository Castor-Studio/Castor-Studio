using CastorCore.Input.Video;
using CastorCore.Source.Video.Pipe;
using FFMpegCore.Pipes;

namespace CastorCore.Source.Video
{
    public class VideoSource : IVideoSource
    {
        private readonly IVideoInput _input;

        public int Width => _input.Width;
        public int Height => _input.Height;
        public double FrameRate => _input.FrameRate;

        public VideoSource(IVideoInput input)
        {
            _input = input;
        }

        public void StartCapture() => _input.StartCapture();
        public void StopCapture() => _input.StopCapture();

        public IEnumerable<IVideoFrame> GetVideoFrames()
        {
            foreach (IVideoFrame sample in _input.PullFrames())
            {
                yield return sample;
            }
        }

        public IPipeSource ToPipeSource()
        {
            // Use our custom BufferedVideoPipeSource instead of RawVideoPipeSource
            // This avoids the "Enumerator is empty" error
            return new BufferedVideoPipeSource(
                GetVideoFrames(),
                _input.Width,
                _input.Height,
                _input.FrameRate
            );
        }
    }
}
