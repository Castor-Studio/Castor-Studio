using FFMpegCore.Pipes;

namespace CastorCore.Source.Video
{
    public interface IVideoSource
    {
        int Width { get; }
        int Height { get; }
        double FrameRate { get; }

        void StartCapture();
        void StopCapture();

        IEnumerable<IVideoFrame> GetVideoFrames();
        IPipeSource ToPipeSource();
    }
}
