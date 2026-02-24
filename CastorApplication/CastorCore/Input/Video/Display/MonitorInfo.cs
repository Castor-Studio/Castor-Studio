namespace CastorCore.Input.Video.Display
{
    public class MonitorInfo
    {
        public int GlobalIndex { get; set; }
        public uint OutputId { get; set; }
        public uint AdapterId { get; set; }

        public string DeviceName { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsAttached { get; set; }
        public string Rotation { get; set; } = string.Empty;
    }
}
