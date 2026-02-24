using CastorCore.Input.Video;
using CastorCore.Input.Video.Display;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CastorCore.Source.Video
{
    public class VideoSourceFactory
    {
        public static IVideoSource CreateDisplaySource(int displayIndex = 0)
        {
            IVideoInput displayInput = new DxgiDisplayCapture(displayIndex);
            return new VideoSource(displayInput);
        }
    }
}
