using CastorCore.Input.Video.Display;
using CastorCore.Source.Video;
using FFMpegCore.Pipes;

namespace CastorCoreTests.Source.Video
{
    [Collection("DxgiCapture")]
    public class VideoSourceWithDxgiTests
    {
        [Fact]
        public void TestVideoSourceWithRealCapture()
        {
            Thread.Sleep(100);
            
            using var input = new DxgiDisplayCapture(0);
            var source = new VideoSource(input);

            source.StartCapture();

            List<IVideoFrame> capturedFrames = new List<IVideoFrame>();
            int targetFrames = 30;
            
            try
            {
                foreach (var frame in source.GetVideoFrames())
                {
                    capturedFrames.Add(frame);
                    
                    if (capturedFrames.Count >= targetFrames)
                    {
                        break;
                    }
                }
            }
            finally
            {
                source.StopCapture();
            }

            Assert.True(capturedFrames.Count >= 10, $"Expected at least 10 frames, got {capturedFrames.Count}");
            
            foreach (var frame in capturedFrames)
            {
                Assert.Equal(input.Width, frame.Width);
                Assert.Equal(input.Height, frame.Height);
                
                if (frame is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        [Fact]
        public void TestVideoSourceStopsCleanly()
        {
            Thread.Sleep(100);
            
            using DxgiDisplayCapture input = new DxgiDisplayCapture(0);
            VideoSource source = new VideoSource(input);

            source.StartCapture();

            List<IVideoFrame> capturedFrames = new List<IVideoFrame>();

            Task task = Task.Run(() =>
            {
                foreach (IVideoFrame frame in source.GetVideoFrames())
                {
                    capturedFrames.Add(frame);
                    
                    if (capturedFrames.Count >= 5)
                    {
                        break;
                    }
                }
            });

            // Wait for some frames to be captured
            bool completed = task.Wait(TimeSpan.FromSeconds(5));
            source.StopCapture();

            Assert.True(completed, "Capture task should complete");
            Assert.True(capturedFrames.Count > 0, "Should have captured at least one frame");
            
            foreach (IVideoFrame frame in capturedFrames)
            {
                if (frame is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        [Fact]
        public void TestVideoSourceFrameCapture()
        {
            Thread.Sleep(100);
            
            using DxgiDisplayCapture input = new DxgiDisplayCapture(0);
            VideoSource source = new VideoSource(input);

            source.StartCapture();

            int frameCount = 0;
            int targetFrames = 60;
            
            try
            {
                foreach (IVideoFrame frame in source.GetVideoFrames())
                {
                    frameCount++;
                    
                    Assert.Equal(input.Width, frame.Width);
                    Assert.Equal(input.Height, frame.Height);
                    Assert.Equal("bgra32", frame.Format);
                    
                    if (frame is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                    
                    if (frameCount >= targetFrames)
                    {
                        break;
                    }
                }
            }
            finally
            {
                source.StopCapture();
            }

            Assert.True(frameCount >= targetFrames, $"Expected at least {targetFrames} frames, got {frameCount}");
        }

        [Fact]
        public void TestVideoSourceProperties()
        {
            Thread.Sleep(100);
            
            using DxgiDisplayCapture input = new DxgiDisplayCapture(0);
            VideoSource source = new VideoSource(input);

            // Assert - Verify properties are correctly exposed
            Assert.True(source.Width > 0, "Width should be greater than 0");
            Assert.True(source.Height > 0, "Height should be greater than 0");
            Assert.Equal(60.0, source.FrameRate);
            Assert.Equal(input.Width, source.Width);
            Assert.Equal(input.Height, source.Height);
        }

        [Fact]
        public void TestVideoSourceToPipeSource()
        {
            Thread.Sleep(100);
            
            using DxgiDisplayCapture input = new DxgiDisplayCapture(0);
            VideoSource source = new VideoSource(input);

            // Act
            var pipeSource = source.ToPipeSource();

            // Assert
            Assert.NotNull(pipeSource);
            
            string args = pipeSource.GetStreamArguments();
            Assert.Contains("-f rawvideo", args);
            Assert.Contains("-pix_fmt bgra", args);
            Assert.Contains($"-video_size {input.Width}x{input.Height}", args);
            Assert.Contains($"-framerate {input.FrameRate}", args);
        }
    }
}
