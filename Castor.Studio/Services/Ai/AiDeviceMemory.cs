using System.Diagnostics;
using System.Text.Json;
using CastorApplication.Services.Settings;

namespace CastorApplication.Services.Ai;

/// <summary>
/// Remembers, per user, the GPUs that loaded a model too slowly, so later starts go straight to the
/// CPU. Keyed by adapter name: another GPU gets its own attempt.
/// </summary>
internal sealed class AiDeviceMemory(string? filePath = null)
{
    private readonly string _filePath = filePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "castor-studio", "ai-device.json");

    public bool PrefersCpu(string adapterName) => Load().CpuAdapters.Contains(adapterName);

    public void RememberCpu(string adapterName)
    {
        var state = Load();
        if (state.CpuAdapters.Contains(adapterName)) return;
        state.CpuAdapters.Add(adapterName);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            SettingsService.WriteFileAtomically(_filePath, JsonSerializer.Serialize(state));
        }
        catch (Exception exception)
        {
            // Not remembered: the next start tries the GPU again.
            Debug.WriteLine($"[AiDeviceMemory] Failed to save {_filePath}: {exception.Message}");
        }
    }

    private State Load()
    {
        try
        {
            if (File.Exists(_filePath))
                return JsonSerializer.Deserialize<State>(File.ReadAllText(_filePath)) ?? new State();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[AiDeviceMemory] Failed to read {_filePath}: {exception.Message}");
        }
        return new State();
    }

    private sealed class State
    {
        public List<string> CpuAdapters { get; set; } = [];
    }
}
