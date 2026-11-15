namespace CastorApplication.Models.Settings;

/// <summary>Where the local AI runs. Stored as an int: keep Auto first, it is the default.</summary>
public enum AiDevice
{
    // GPU, restarted on the CPU when the model loads too slowly.
    Auto,
    Cpu,
    // DirectML, with the worker's own CPU fallback only when a model cannot load.
    Gpu,
}
