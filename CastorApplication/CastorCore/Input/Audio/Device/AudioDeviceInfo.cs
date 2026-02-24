namespace CastorCore.Input.Audio.Device
{
    public class AudioDeviceInfo
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public AudioDeviceType Type { get; init; }
        public int Channels { get; init; }
        public int SampleRate { get; init; }
    }
}