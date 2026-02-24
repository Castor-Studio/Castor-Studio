using CastorCore.Input.Video.Display;

namespace CastorCore.Input.Video
{
    public class VideoInputFactory
    {
        public static IVideoInput CreateDxgiDisplayInput(int displayIndex = 0)
        {
            return new DxgiDisplayCapture(displayIndex);
        }
    }
}
