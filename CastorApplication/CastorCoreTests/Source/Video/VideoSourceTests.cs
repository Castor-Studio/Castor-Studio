using CastorCore.Source.Video;
using CastorCoreTests.Mock;
using FFMpegCore.Pipes;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CastorCoreTests.Source.Video
{
    public class VideoSourceTests
    {
        [Fact]
        public void TestVideoSourceWithMockInput()
        {
            // Arrange
            MockVideoInput mockInput = new MockVideoInput();
            VideoSource testSource = new VideoSource(mockInput);
            
            List<IVideoFrame> capturedFrames = new List<IVideoFrame>();
            int targetFrames = 5;

            // Act
            testSource.StartCapture();
            
            foreach (IVideoFrame frame in testSource.GetVideoFrames())
            {
                capturedFrames.Add(frame);
                
                if (capturedFrames.Count >= targetFrames)
                {
                    break;
                }
            }
            
            testSource.StopCapture();

            // Assert
            Assert.True(capturedFrames.Count >= targetFrames, $"Expected at least {targetFrames} frames, got {capturedFrames.Count}");
            
            foreach (IVideoFrame frame in capturedFrames)
            {
                Assert.Equal(640, frame.Width);
                Assert.Equal(480, frame.Height);
                Assert.Equal("bgra32", frame.Format);
                
                // Dispose frames after use
                if (frame is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        [Fact]
        public void TestVideoSourceDispose()
        {
            // Arrange
            MockVideoInput mockInput = new MockVideoInput();
            VideoSource testSource = new VideoSource(mockInput);

            // Act
            mockInput.Dispose();

            // Assert - Should not throw
            Assert.True(true);
        }

        [Fact]
        public void TestVideoSourceMultipleFrames()
        {
            // Arrange
            MockVideoInput mockInput = new MockVideoInput();
            VideoSource testSource = new VideoSource(mockInput);
            
            List<IVideoFrame> capturedFrames = new List<IVideoFrame>();
            int targetFrames = 10;

            // Act
            testSource.StartCapture();
            
            foreach (IVideoFrame frame in testSource.GetVideoFrames())
            {
                capturedFrames.Add(frame);
                
                if (capturedFrames.Count >= targetFrames)
                {
                    break;
                }
            }
            
            testSource.StopCapture();

            // Assert
            Assert.True(capturedFrames.Count >= targetFrames, $"Expected at least {targetFrames} frames, got {capturedFrames.Count}");
            
            // Cleanup
            foreach (IVideoFrame frame in capturedFrames)
            {
                if (frame is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        [Fact]
        public void TestVideoSourceStopsOnStopCapture()
        {
            // Arrange
            MockVideoInput mockInput = new MockVideoInput();
            VideoSource testSource = new VideoSource(mockInput);
            
            List<IVideoFrame> capturedFrames = new List<IVideoFrame>();
            bool enumerationCompleted = false;

            // Act
            testSource.StartCapture();

            Task captureTask = Task.Run(() =>
            {
                foreach (IVideoFrame frame in testSource.GetVideoFrames())
                {
                    capturedFrames.Add(frame);
                    
                    if (capturedFrames.Count >= 3)
                    {
                        // Stop capture after a few frames
                        testSource.StopCapture();
                    }
                }
                enumerationCompleted = true;
            });

            // Wait for completion with timeout
            bool completed = captureTask.Wait(TimeSpan.FromSeconds(5));
            
            // Assert
            Assert.True(completed, "Capture task should complete within timeout");
            Assert.True(enumerationCompleted, "Enumeration should complete");
            Assert.True(capturedFrames.Count >= 3, $"Expected at least 3 frames, got {capturedFrames.Count}");
            
            // Cleanup
            foreach (IVideoFrame frame in capturedFrames)
            {
                if (frame is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        [Fact]
        public void TestVideoSourceProperties()
        {
            // Arrange
            MockVideoInput mockInput = new MockVideoInput(width: 1920, height: 1080);
            VideoSource testSource = new VideoSource(mockInput);

            // Assert
            Assert.Equal(1920, testSource.Width);
            Assert.Equal(1080, testSource.Height);
            Assert.Equal(30.0, testSource.FrameRate);
        }

        [Fact]
        public void TestVideoSourceToPipeSource()
        {
            // Arrange
            MockVideoInput mockInput = new MockVideoInput();
            VideoSource testSource = new VideoSource(mockInput);

            // Act
            var pipeSource = testSource.ToPipeSource();

            // Assert
            Assert.NotNull(pipeSource);
            
            string args = pipeSource.GetStreamArguments();
            Assert.Contains("-f rawvideo", args);
            Assert.Contains("-pix_fmt bgra", args);
            Assert.Contains($"-video_size {mockInput.Width}x{mockInput.Height}", args);
            Assert.Contains($"-framerate {mockInput.FrameRate}", args);
        }
    }
}
