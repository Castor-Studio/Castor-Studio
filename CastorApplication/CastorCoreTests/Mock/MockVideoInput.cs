using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CastorCore.Input.Video;
using FFMpegCore.Pipes;

namespace CastorCoreTests.Mock
{
    public class MockVideoInput : IVideoInput
    {
        private readonly int _frameDelay;
        private int _framesCaptured;
        private volatile bool _isCapturing;

        public int Width { get; }
        public int Height { get; }
        public double FrameRate => 30.0;

        /// <summary>
        /// Number of frames captured by this input
        /// </summary>
        public int FramesCaptured => _framesCaptured;

        public MockVideoInput(int width = 640, int height = 480, int frameDelay = 33)
        {
            Width = width;
            Height = height;
            _frameDelay = frameDelay;
        }

        public void StartCapture()
        {
            _isCapturing = true;
        }

        public void StopCapture()
        {
            _isCapturing = false;
        }

        public IEnumerable<IVideoFrame> PullFrames()
        {
            while (_isCapturing)
            {
                if (_frameDelay > 0)
                {
                    Thread.Sleep(_frameDelay);
                }

                MockVideoFrame frame = new MockVideoFrame(Width, Height);
                _framesCaptured++;

                yield return frame;
            }
        }

        public void Dispose()
        {
            StopCapture();
        }
    }
}
