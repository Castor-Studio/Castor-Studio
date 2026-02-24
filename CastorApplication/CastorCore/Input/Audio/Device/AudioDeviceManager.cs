using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CastorCore.Input.Audio.Device
{
    public class AudioDeviceManager
    {
        private readonly MMDeviceEnumerator _enumerator = new();

        public IReadOnlyList<MMDevice> GetInputDevices()
        {
            MMDeviceCollection devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

            return devices.ToList();
        }

        public IReadOnlyList<MMDevice> GetOutputDevices()
        {
            MMDeviceCollection devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

            return devices.ToList();
        }

        public MMDevice GetDefaultOutputDevice()
        {
            return _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
        }

        public MMDevice GetDefaultInputDevice()
        {
            return _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
        }

        public IReadOnlyList<AudioDeviceInfo> GetInputDevicesInfo()
        {
            MMDeviceCollection devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

            List<AudioDeviceInfo> result = new List<AudioDeviceInfo>();
            foreach (MMDevice? dev in devices)
            {
                WaveFormat format = dev.AudioClient.MixFormat;

                result.Add(new AudioDeviceInfo
                {
                    Id = dev.ID,
                    Name = dev.FriendlyName,
                    Type = AudioDeviceType.Input,
                    Channels = format.Channels,
                    SampleRate = format.SampleRate
                });
            }

            return result;
        }

        public IReadOnlyList<AudioDeviceInfo> GetOutputDeviceInfos()
        {
            MMDeviceCollection devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

            List<AudioDeviceInfo> result = new List<AudioDeviceInfo>();
            foreach (MMDevice? dev in devices)
            {
                WaveFormat format = dev.AudioClient.MixFormat;

                result.Add(new AudioDeviceInfo
                {
                    Id = dev.ID,
                    Name = dev.FriendlyName,
                    Type = AudioDeviceType.Output,
                    Channels = format.Channels,
                    SampleRate = format.SampleRate
                });
            }

            return result;
        }

        public AudioDeviceInfo? GetDeviceInfoById(string id)
        {
            try
            {
                MMDevice dev = _enumerator.GetDevice(id);
                WaveFormat format = dev.AudioClient.MixFormat;

                return new AudioDeviceInfo
                {
                    Id = dev.ID,
                    Name = dev.FriendlyName,
                    Type = dev.DataFlow == DataFlow.Capture ? AudioDeviceType.Input : AudioDeviceType.Output,
                    Channels = format.Channels,
                    SampleRate = format.SampleRate
                };
            }
            catch
            {
                return null;
            }
        }
    }
}
