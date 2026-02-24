using CastorCore.Input.Audio;
using CastorCore.Input.Audio.Device;
using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CastorCore.Source.Audio
{
    public class AudioSourceFactory
    {
        public static IAudioSource CreateDefaultOutputAudioSource()
        {
            AudioDeviceManager audioDeviceManager = new AudioDeviceManager();
            MMDevice device = audioDeviceManager.GetDefaultOutputDevice();
            WasapiAudioCapture audioInput = new WasapiAudioCapture(device);

            return new AudioSource(audioInput);
        }
    }
}
