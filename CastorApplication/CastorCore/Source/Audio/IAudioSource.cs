using FFMpegCore.Pipes;

namespace CastorCore.Source.Audio
{
    public interface IAudioSource
    {
        void StartCapture();
        void StopCapture();

        IEnumerator<IAudioSample> GetAudioSamples();
        IPipeSource ToPipeSource();
    }
}