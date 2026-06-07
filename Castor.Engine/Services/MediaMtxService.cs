using System.Diagnostics;

namespace Castor.Engine.Services;

public sealed class MediaMtxService : IMediaMtxService
{
    private const string RtmpBase = "rtmp://127.0.0.1:1935/live/";

    private Process? _process;

    public bool IsRunning => Process.GetProcessesByName("mediamtx").Any();

    public string GetPreviewPushUrl(Guid sceneId) => $"{RtmpBase}{sceneId:N}";

    public string GetPreviewPullUrl(Guid sceneId) => $"{RtmpBase}{sceneId:N}";

    public void Start()
    {
        if (IsRunning) return;

        string exePath = Path.Combine(AppContext.BaseDirectory, "mediamtx.exe");
        if (!File.Exists(exePath))
        {
            Debug.WriteLine($"[MediaMTX] mediamtx.exe introuvable : {exePath}");
            return;
        }

        foreach (var orphan in Process.GetProcessesByName("mediamtx"))
        {
            try
            {
                orphan.Kill();
                orphan.WaitForExit(2000);
                Debug.WriteLine($"[MediaMTX] Instance orpheline tuée (PID {orphan.Id}).");
            }
            catch
            {
            }
            finally
            {
                orphan.Dispose();
            }
        }

        var psi = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => { if (e.Data != null) Debug.WriteLine($"[MediaMTX] {e.Data}"); };
        _process.ErrorDataReceived += (_, e) => { if (e.Data != null) Debug.WriteLine($"[MediaMTX] {e.Data}"); };
        _process.Exited += (_, _) => Debug.WriteLine("[MediaMTX] Processus terminé.");

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        Thread.Sleep(800);

        Debug.WriteLine($"[MediaMTX] Démarré (PID {_process.Id}), IsRunning={IsRunning}.");
    }

    public void Stop()
    {
        foreach (var process in Process.GetProcessesByName("mediamtx"))
        {
            try
            {
                process.Kill();
                process.WaitForExit(2000);
                Debug.WriteLine($"[MediaMTX] Arrêté (PID {process.Id}).");
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        _process?.Dispose();
        _process = null;
    }
}
