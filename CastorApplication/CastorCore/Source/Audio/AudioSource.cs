using CastorCore.Input.Audio;
using FFMpegCore.Pipes;
using NAudio.Wave;

namespace CastorCore.Source.Audio
{
    public class AudioSource : IAudioSource
    {
        private readonly IAudioInput _input;

        public AudioSource(IAudioInput input)
        {
            _input = input;
        }

        public void StartCapture() => _input.StartCapture();
        public void StopCapture() => _input.StopCapture();

        public IEnumerator<IAudioSample> GetAudioSamples()
        {
            foreach (IAudioSample sample in _input.PullSamples())
            {
                yield return sample;
            }
        }

        public IPipeSource ToPipeSource()
        {
            WaveFormat waveFormat = _input.Device.AudioClient.MixFormat;

            IPipeSource rawAudioPipeSource = new RawAudioPipeSource(GetAudioSamples())
            {
                Channels = (uint)waveFormat.Channels,
                SampleRate = (uint)waveFormat.SampleRate,
            };

            return rawAudioPipeSource;
        }
    }
}
