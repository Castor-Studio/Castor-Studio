using FFMpegCore.Pipes;

namespace CastorCoreTests.Mock
{
    public class MockVideoFrame : IVideoFrame
    {
        private byte[] _data;

        public int Width { get; }
        public int Height { get; }
        public string Format { get; } = "bgra32";

        public MockVideoFrame(int width, int height)
        {
            Width = width;
            Height = height;
            _data = new byte[width * height * 4];

            // Fill with a test pattern
            for (int i = 0; i < _data.Length; i += 4)
            {
                _data[i] = 0;      // B
                _data[i + 1] = 128; // G
                _data[i + 2] = 255; // R
                _data[i + 3] = 255; // A
            }
        }

        public void Serialize(Stream pipe)
        {
            pipe.Write(_data, 0, _data.Length);
        }

        public async Task SerializeAsync(Stream pipe, CancellationToken token)
        {
            await pipe.WriteAsync(_data, 0, _data.Length, token);
        }
    }
}
