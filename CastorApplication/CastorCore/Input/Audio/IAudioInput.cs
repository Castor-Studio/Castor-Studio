using FFMpegCore.Pipes;
using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CastorCore.Input.Audio
{
    public interface IAudioInput
    {
        void StartCapture();
        void StopCapture();

        public MMDevice Device { get; }

        IEnumerable<IAudioSample> PullSamples();
    }
}
