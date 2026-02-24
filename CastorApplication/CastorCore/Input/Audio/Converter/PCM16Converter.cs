using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CastorCore.Input.Audio.Converter
{
    public class PCM16Converter : IAudioConverter
    {
        private const int TARGET_BITS_PER_SAMPLE = 16;

        public bool IsConversionNeeded(WaveFormat sourceFormat, WaveFormat targetFormat)
        {
            return !(sourceFormat.Encoding == WaveFormatEncoding.Pcm &&
                     sourceFormat.BitsPerSample == TARGET_BITS_PER_SAMPLE &&
                     sourceFormat.SampleRate == targetFormat.SampleRate &&
                     sourceFormat.Channels == targetFormat.Channels);
        }

        public byte[] Convert(byte[] sourceBuffer, WaveFormat sourceFormat, WaveFormat targetFormat)
        {
            if (!IsConversionNeeded(sourceFormat, targetFormat))
                return sourceBuffer;

            using MemoryStream sourceStream = new (sourceBuffer);
            using RawSourceWaveStream sourceProvider = new (sourceStream, sourceFormat);
            using MediaFoundationResampler resampler = new (sourceProvider, targetFormat);
            using MemoryStream outputStream = new MemoryStream();

            byte[] buffer = new byte[sourceFormat.AverageBytesPerSecond];
            int bytesRead;

            while ((bytesRead = resampler.Read(buffer, 0, buffer.Length)) > 0)
            {
                outputStream.Write(buffer, 0, bytesRead);
            }

            return outputStream.ToArray();
        }
    }
}
