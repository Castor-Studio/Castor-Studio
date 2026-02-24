using CastorCore.Frame;
using FFMpegCore.Pipes;
using SharpGen.Runtime;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace CastorCore.Input.Video.Display
{
    public class DxgiDisplayCapture : IVideoInput, IDisposable
    {
        private IDXGIOutputDuplication? _duplication;
        private ID3D11Device? _device;
        private ID3D11DeviceContext? _context;
        private ID3D11Texture2D? _staging;

        private Thread? _worker;
        private volatile bool _running;
        private readonly ConcurrentQueue<IVideoFrame> _queue = new();
        private const int MAX_QUEUE_SIZE = 120;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public double FrameRate => 60.0;
        public string Format => "bgra32";

        public DxgiDisplayCapture(int globalIndex = 0)
        {
            List<MonitorInfo> monitors = MonitorManager.GetMonitorInfos();

            if (globalIndex < 0 || globalIndex >= monitors.Count)
                throw new ArgumentOutOfRangeException(nameof(globalIndex));

            MonitorInfo monitor = monitors[globalIndex];
            InitializeCapture(monitor.AdapterId, monitor.OutputId);

            Width = monitor.Width;
            Height = monitor.Height;
        }

        public IEnumerable<IVideoFrame> PullFrames()
        {
            Console.WriteLine($"[PULL] PullFrames() started. Running: {_running}, Queue: {_queue.Count}");
            int frameCount = 0;

            while (_running || _queue.Count > 0)
            {
                if (_queue.TryDequeue(out IVideoFrame? frame))
                {
                    frameCount++;
                    
                    if (frameCount == 1)
                    {
                        Console.WriteLine($"[PULL] First frame dequeued! Queue size: {_queue.Count}");
                    }
                    else if (frameCount % 60 == 0)
                    {
                        Console.WriteLine($"[PULL] {frameCount} frames dequeued, queue size: {_queue.Count}");
                    }
                    
                    yield return frame;
                }
                else if (!_running)
                {
                    Console.WriteLine($"[PULL] Finished. Total frames pulled: {frameCount}");
                    yield break;
                }
                else
                {
                    Thread.Sleep(1);
                }
            }
            
            Console.WriteLine($"[PULL] Exited loop. Total frames pulled: {frameCount}");
        }

        public void StartCapture()
        {
            if (_running)
                return;

            _running = true;
            _worker = new Thread(CaptureLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal // Increase priority to reduce frame drops
            };

            _worker.Start();
        }

        public void StopCapture()
        {
            _running = false;
            _worker?.Join();
        }

        private void InitializeCapture(uint adapterId, uint outputId)
        {
            using IDXGIFactory1 factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

            factory.EnumAdapters1(adapterId, out IDXGIAdapter1? adapter)
                .CheckError();

            using (adapter)
            {
                adapter.EnumOutputs(outputId, out IDXGIOutput? output)
                      .CheckError();

                using (output)
                {
                    OutputDescription desc = output.Description;
                    Width = desc.DesktopCoordinates.Right - desc.DesktopCoordinates.Left;
                    Height = desc.DesktopCoordinates.Bottom - desc.DesktopCoordinates.Top;

                    D3D11.D3D11CreateDevice(
                        adapter,
                        Vortice.Direct3D.DriverType.Unknown,
                        DeviceCreationFlags.BgraSupport,
                        null,
                        out ID3D11Device? device
                    ).CheckError();

                    _device = device ?? throw new InvalidOperationException("Unable to create D3D11 device");
                    _context = _device.ImmediateContext;

                    using IDXGIOutput1 output1 = output.QueryInterface<IDXGIOutput1>();
                    _duplication = output1.DuplicateOutput(_device)
                        ?? throw new InvalidOperationException("Failed to duplicate output");

                    CreateStagingTexture();
                }
            }
        }

        private void CreateStagingTexture()
        {
            _staging?.Dispose();

            Texture2DDescription desc = new Texture2DDescription
            {
                Format = Vortice.DXGI.Format.B8G8R8A8_UNorm,
                Width = (uint)Width,
                Height = (uint)Height,
                MipLevels = 1,
                ArraySize = 1,
                SampleDescription = new SampleDescription(1, 0),
                BindFlags = BindFlags.None,
                CPUAccessFlags = CpuAccessFlags.Read,
                Usage = ResourceUsage.Staging
            };

            _staging = _device!.CreateTexture2D(desc);
        }

        private void CaptureLoop()
        {
            double targetFrameTime = 1000.0 / FrameRate; // 60fps = 16.67ms for each frame
            Stopwatch stopwatch = Stopwatch.StartNew();
            double nextFrameTime = 0;

            byte[]? lastFrameData = null;
            int framesCaptured = 0;
            int framesDuplicated = 0;
            var logTimer = Stopwatch.StartNew();

            Console.WriteLine($"[CAPTURE] CaptureLoop started. Target FPS: {FrameRate}");

            while (_running)
            {
                double currentTime = stopwatch.Elapsed.TotalMilliseconds;

                if (currentTime >= nextFrameTime)
                {
                    IVideoFrame? frame = TryCaptureFrame();

                    if (frame != null)
                    {
                        lastFrameData = ((RawVideoFrame)frame).Data;
                        framesCaptured++;

                        if (_queue.Count < MAX_QUEUE_SIZE)
                            _queue.Enqueue(frame);
                    }
                    else if (lastFrameData != null)
                    {
                        // No new frame captured, duplicate the last frame
                        byte[] duplicatedData = new byte[lastFrameData.Length];
                        Array.Copy(lastFrameData, duplicatedData, lastFrameData.Length);

                        IVideoFrame duplicatedFrame = new RawVideoFrame(MediaTimestamp.Now(), Width, Height, duplicatedData);
                        framesDuplicated++;

                        if (_queue.Count < MAX_QUEUE_SIZE)
                            _queue.Enqueue(duplicatedFrame);
                    }

                    // Log every second
                    if (logTimer.ElapsedMilliseconds >= 1000)
                    {
                        var totalFrames = framesCaptured + framesDuplicated;
                        var actualFps = totalFrames / stopwatch.Elapsed.TotalSeconds;
                        Console.WriteLine($"[CAPTURE] FPS: {actualFps:F2} | Queue: {_queue.Count} | Captured: {framesCaptured} | Duplicated: {framesDuplicated}");
                        logTimer.Restart();
                    }

                    nextFrameTime += targetFrameTime;

                    // Prevent lag accumulation
                    if (nextFrameTime < currentTime)
                        nextFrameTime = currentTime + targetFrameTime;
                }
                else
                {
                    // Wait until it's time for the next frame
                    double sleepTime = nextFrameTime - currentTime;

                    if (sleepTime > 1)
                        Thread.Sleep((int)(sleepTime - 1));
                    else
                        Thread.SpinWait(10);
                }
            }

            Console.WriteLine($"[CAPTURE] CaptureLoop ended. Total captured: {framesCaptured}, duplicated: {framesDuplicated}");
        }

        private IVideoFrame? TryCaptureFrame()
        {
            if (_duplication == null)
                return null;

            Result result = _duplication.AcquireNextFrame(
                0, // no timeout = immediate return
                out OutduplFrameInfo frameInfo,
                out IDXGIResource? resource
            );

            //TODO: Add more clear error check here
            if (result.Code == unchecked((int)0x887A0026)) // DXGI_ERROR_ACCESS_LOST
            {
                Console.WriteLine("[DXGI] Access lost – stopping capture");
                return null;
            }

            if (result.Failure || resource == null)
                return null;

            using (resource)
            using (ID3D11Texture2D tex = resource.QueryInterface<ID3D11Texture2D>())
            {
                _context!.CopyResource(_staging!, tex);
                _duplication.ReleaseFrame();

                return MapFrame();
            }
        }

        private IVideoFrame MapFrame()
        {
            MappedSubresource map = _context!.Map(_staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);

            uint srcStride = map.RowPitch;
            int dstStride = Width * 4;
            int size = dstStride * Height;

            byte[] buffer = new byte[size];

            unsafe
            {
                byte *srcBase = (byte *)map.DataPointer;
                fixed (byte* dstBase = buffer)
                {
                    for (int y = 0; y < Height; y++)
                    {
                        byte* src = srcBase + y * srcStride;
                        byte* dst = dstBase + y * dstStride;

                        Buffer.MemoryCopy(src, dst, dstStride, dstStride);
                    }
                }
            }

            _context.Unmap(_staging!, 0);

            return new RawVideoFrame(MediaTimestamp.Now(), Width, Height, buffer);
        }

        public void Dispose()
        {
            StopCapture();

            _staging?.Dispose();
            _duplication?.Dispose();
            _context?.Dispose();
            _device?.Dispose();
        }
    }
}
