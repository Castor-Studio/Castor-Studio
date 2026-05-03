using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace CastorApplication.Services;

/// <summary>
/// Gère le cycle de vie du processus MediaMTX (serveur RTMP local).
/// MediaMTX reçoit le flux poussé par libcastor et le redistribue
/// au player VLC intégré (et à l'IA via RTMP/RTSP).
/// </summary>
public sealed class MediaMtxService
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    private static MediaMtxService? _instance;
    public static MediaMtxService Instance => _instance ??= new MediaMtxService();

    // ── URLs ──────────────────────────────────────────────────────────────────

    private const string RtmpBase = "rtmp://127.0.0.1:1935/live/";

    /// <summary>URL vers laquelle libcastor pousse le flux de preview d'une scène.</summary>
    public static string GetPreviewPushUrl(Guid sceneId) => $"{RtmpBase}{sceneId:N}";

    /// <summary>URL depuis laquelle VLC (et l'IA) tire le flux de preview d'une scène.</summary>
    public static string GetPreviewPullUrl(Guid sceneId) => $"{RtmpBase}{sceneId:N}";

    // ── État ──────────────────────────────────────────────────────────────────
    private Process? _process;

    // mediamtx se daemonise : le parent quitte, un enfant reste.
    // On vérifie donc si un process mediamtx est actif sur le système,
    // pas uniquement si notre handle parent est vivant.
    public bool IsRunning => Process.GetProcessesByName("mediamtx").Any();

    private MediaMtxService() { }

    // ── API publique ──────────────────────────────────────────────────────────

    /// <summary>
    /// Démarre mediamtx.exe depuis le répertoire de l'application.
    /// Sans effet si MediaMTX tourne déjà.
    /// </summary>
    public void Start()
    {
        if (IsRunning) return;

        string exePath = Path.Combine(AppContext.BaseDirectory, "mediamtx.exe");
        if (!File.Exists(exePath))
        {
            Debug.WriteLine($"[MediaMTX] mediamtx.exe introuvable : {exePath}");
            return;
        }

        // Tue toute instance orpheline (crash précédent, arrêt brutal, etc.)
        // pour libérer le port 1935 avant de démarrer une nouvelle instance.
        foreach (var orphan in Process.GetProcessesByName("mediamtx"))
        {
            try
            {
                orphan.Kill();
                orphan.WaitForExit(2000);
                Debug.WriteLine($"[MediaMTX] Instance orpheline tuée (PID {orphan.Id}).");
            }
            catch { /* déjà terminé */ }
            finally { orphan.Dispose(); }
        }

        var psi = new ProcessStartInfo(exePath)
        {
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => { if (e.Data != null) Debug.WriteLine($"[MediaMTX] {e.Data}"); };
        _process.ErrorDataReceived  += (_, e) => { if (e.Data != null) Debug.WriteLine($"[MediaMTX] {e.Data}"); };
        _process.Exited             += (_, _) => Debug.WriteLine("[MediaMTX] Processus terminé.");

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        // Laisse MediaMTX initialiser ses listeners RTMP avant que libcastor pousse
        System.Threading.Thread.Sleep(800);

        Debug.WriteLine($"[MediaMTX] Démarré (PID {_process.Id}), IsRunning={IsRunning}.");
    }

    /// <summary>
    /// Arrête MediaMTX proprement.
    /// Tue tous les processus mediamtx sur le système (parent + daemon enfant).
    /// Sans effet si aucune instance n'est active.
    /// </summary>
    public void Stop()
    {
        // mediamtx se daemonise : _process est déjà exité (parent),
        // mais un enfant daemon tourne encore. On tue par nom de processus.
        foreach (var p in Process.GetProcessesByName("mediamtx"))
        {
            try
            {
                p.Kill();
                p.WaitForExit(2000);
                Debug.WriteLine($"[MediaMTX] Arrêté (PID {p.Id}).");
            }
            catch { /* déjà terminé */ }
            finally { p.Dispose(); }
        }

        _process?.Dispose();
        _process = null;
    }
}
