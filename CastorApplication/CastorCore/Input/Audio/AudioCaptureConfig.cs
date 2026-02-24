using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CastorCore.Input.Audio
{
    public class AudioCaptureConfig
    {
        /// <summary>
        /// Output audio format
        /// </summary>
        public AudioFormat OutputFormat { get; set; } = AudioFormat.PCM16;

        /// <summary>
        /// Sample rate for audio processing (0 = keep original sample rate)
        /// </summary>
        /// <remarks>
        /// The sample rate determines the number of audio samples per second. Ensure that the
        /// value is compatible with the audio source and processing requirements.
        /// </remarks>
        public int SampleRate { get; set; } = 0;

        /// <summary>
        /// Audio channels for processing (0 = keep original channel count)
        /// </summary>
        public int Channels { get; set; } = 0;

        /// <summary>
        /// Buffer size in milliseconds
        /// </summary>
        public int BufferDurationMs { get; set; } = 100;

        /// <summary>
        /// Driver audio share mode
        /// </summary>
        public AudioClientShareMode ShareMode { get; set; } = AudioClientShareMode.Shared;
    }
}
