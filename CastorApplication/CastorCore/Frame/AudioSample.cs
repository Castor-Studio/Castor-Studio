using FFMpegCore.Pipes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CastorCore.Frame
{
    public class AudioSample : IAudioSample
    {
        public byte[] Data { get; }
        public int Channels { get; }
        public int SampleRate { get; }
        public int SampleCount => Data.Length / (Channels * 2); // Assuming 16-bit
        public MediaTimestamp Timestamp { get; }

        public int Width => 0;
        public int Height => 0;
        public string Format => "s16le";

        public AudioSample(byte[] data, int channels, int sampleRate)
            : this(data, channels, sampleRate, MediaTimestamp.Zero)
        {
        }

        public AudioSample(byte[] data, int channels, int sampleRate, MediaTimestamp timestamp)
        {
            Data = data;
            Channels = channels;
            SampleRate = sampleRate;
            Timestamp = timestamp;
        }

        public void Serialize(Stream stream)
        {
            stream.Write(Data, 0, Data.Length);
        }

        public async Task SerializeAsync(Stream stream, CancellationToken token)
        {
            await stream.WriteAsync(Data, token);
        }
    }
}
