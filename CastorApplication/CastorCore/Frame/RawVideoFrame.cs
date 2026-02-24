using FFMpegCore.Pipes;

namespace CastorCore.Frame
{
    public class RawVideoFrame : IVideoFrame, IDisposable
    {
        private bool _disposed = false;

        public MediaTimestamp Timestamp { get; }
        public int Width { get; }
        public int Height { get; }
        public string Format => "bgra32";
        public byte[] Data { get; private set; }

        public RawVideoFrame(MediaTimestamp ts, int width, int height, byte[] data)
        {
            Timestamp = ts;
            Width = width;
            Height = height;
            Data = data;
        }

        public void Serialize(Stream pipe)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RawVideoFrame));
            
            if (Data == null || Data.Length == 0)
                throw new InvalidOperationException("Frame data not initialized. Call CopyFrom() first.");

            pipe.Write(Data, 0, Data.Length);
        }

        public async Task SerializeAsync(Stream pipe, CancellationToken token)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RawVideoFrame));
            
            if (Data == null || Data.Length == 0)
                throw new InvalidOperationException("Frame data not initialized. Call CopyFrom() first.");

            await pipe.WriteAsync(Data, 0, Data.Length, token);
        }

        public void Dispose()
        {
            if (!_disposed)
                return;

            _disposed = true;
            Data = new byte[0];
        }
    }
}
