using NAudio.Wave;

namespace CastorCore.Input.Audio.Converter
{
    public interface IAudioConverter
    {
        byte[] Convert(byte[] sourceBuffer, WaveFormat sourceFormat, WaveFormat targetFormat);

        bool IsConversionNeeded(WaveFormat sourceFormat, WaveFormat targetFormat);
    }
}
