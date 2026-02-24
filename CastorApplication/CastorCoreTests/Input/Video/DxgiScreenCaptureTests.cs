using CastorCore.Input.Video.Display;
using FFMpegCore.Pipes;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CastorCoreTests.Input.Video
{
    [CollectionDefinition("DxgiCapture", DisableParallelization = true)]
    public class DxgiCaptureCollection
    {
    }

    [Collection("DxgiCapture")]
    public class DxgiScreenCaptureTests
    {
        [Fact]
        public void TestDxgiScreenCaptureInitialization()
        {
            using DxgiDisplayCapture capture = new DxgiDisplayCapture(0);

            Assert.True(capture.Width > 0, "Width should be positive");
            Assert.True(capture.Height > 0, "Height should be positive");
            Assert.Equal("bgra32", capture.Format);
            Assert.Equal(60.0, capture.FrameRate);
        }

        [Fact]
        public void TestCaptureFrame()
        {
            using DxgiDisplayCapture capture = new DxgiDisplayCapture(0);
            
            capture.StartCapture();

            IVideoFrame? frame = null;
            
            try
            {
                foreach (var capturedFrame in capture.PullFrames())
                {
                    frame = capturedFrame;
                    break; // Get just one frame
                }
            }
            finally
            {
                capture.StopCapture();
            }

            Assert.NotNull(frame);
            Assert.Equal(capture.Width, frame.Width);
            Assert.Equal(capture.Height, frame.Height);
            Assert.Equal("bgra32", frame.Format);

            if (frame is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        [Fact]
        public void TestCaptureMultipleFrames()
        {
            Thread.Sleep(100);
            
            using DxgiDisplayCapture capture = new DxgiDisplayCapture(0);
            int frameCount = 0;
            int targetFrames = 60;

            capture.StartCapture();
            
            try
            {
                foreach (IVideoFrame frame in capture.PullFrames())
                {
                    frameCount++;
                    Assert.Equal(capture.Width, frame.Width);
                    Assert.Equal(capture.Height, frame.Height);
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
                capture.StopCapture();
            }

            Assert.True(frameCount >= targetFrames, $"Expected at least {targetFrames} frames, got {frameCount}");
        }

        [Fact]
        public void TestStartStopCapture()
        {
            Thread.Sleep(100);
            
            using DxgiDisplayCapture capture = new DxgiDisplayCapture(0);
            
            // Start and capture a few frames
            capture.StartCapture();
            
            int firstBatchCount = 0;
            foreach (IVideoFrame frame in capture.PullFrames())
            {
                firstBatchCount++;
                
                if (frame is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                
                if (firstBatchCount >= 5)
                {
                    break;
                }
            }
            
            capture.StopCapture();
            
            Assert.True(firstBatchCount >= 5, $"Expected at least 5 frames in first batch, got {firstBatchCount}");
            
            // Start again and capture more frames
            Thread.Sleep(100);
            capture.StartCapture();
            
            int secondBatchCount = 0;
            foreach (IVideoFrame frame in capture.PullFrames())
            {
                secondBatchCount++;
                
                if (frame is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                
                if (secondBatchCount >= 5)
                {
                    break;
                }
            }
            
            capture.StopCapture();
            
            Assert.True(secondBatchCount >= 5, $"Expected at least 5 frames in second batch, got {secondBatchCount}");
        }

        [Fact]
        public void TestInvalidMonitorId()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new DxgiDisplayCapture(999));
        }

        [Fact]
        public void TestPullFramesStopsWhenCaptureStops()
        {
            Thread.Sleep(100);
            
            using DxgiDisplayCapture capture = new DxgiDisplayCapture(0);
            
            capture.StartCapture();
            
            int frameCount = 0;
            bool enumerationCompleted = false;
            
            Task captureTask = Task.Run(() =>
            {
                foreach (IVideoFrame frame in capture.PullFrames())
                {
                    frameCount++;
                    
                    if (frame is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                    
                    if (frameCount >= 10)
                    {
                        // Stop capture from within the enumeration
                        capture.StopCapture();
                    }
                }
                enumerationCompleted = true;
            });
            
            bool completed = captureTask.Wait(TimeSpan.FromSeconds(5));
            
            Assert.True(completed, "Capture task should complete");
            Assert.True(enumerationCompleted, "Enumeration should complete");
            Assert.True(frameCount >= 10, $"Expected at least 10 frames, got {frameCount}");
        }
    }
}
