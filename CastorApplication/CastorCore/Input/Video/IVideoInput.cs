using FFMpegCore.Pipes;

namespace CastorCore.Input.Video
{
    public interface IVideoInput : IDisposable
    {
        int Width { get; }
        int Height { get; }
        double FrameRate { get; }

        void StartCapture();
        void StopCapture();

        IEnumerable<IVideoFrame> PullFrames();
    }
}