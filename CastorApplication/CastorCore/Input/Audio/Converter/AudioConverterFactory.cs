using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CastorCore.Input.Audio.Converter
{
    public class AudioConverterFactory
    {
        public static IAudioConverter CreateConverter(AudioFormat targetFormat)
        {
            return targetFormat switch
            {
                AudioFormat.PCM16 => new PCM16Converter(),
                _ => throw new NotSupportedException($"Format {targetFormat} not supported")
            };
        }
    }
}
