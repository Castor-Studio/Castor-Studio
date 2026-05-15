using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Threading;

namespace CastorApplication.Services;

/// <summary>
/// Wrapper around the ai-hub Python process. Spawns
/// `uv run python main.py --module podcast --no-preview --json-events --sources …`
/// and converts newline-delimited JSON events on stdout into .NET events.
///
/// Decoupled from gRPC for the demo path: we don't need a server in the loop
/// when the front and the AI run on the same machine.
/// </summary>
public sealed class PodcastAiService : IDisposable
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    private static PodcastAiService? _instance;
    public static PodcastAiService Instance => _instance ??= new PodcastAiService();

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Fired (on UI thread) when the AI selects a different source.</summary>
    public event Action<int>? ActiveSourceChanged;

    /// <summary>Fired when the underlying process emits a non-recoverable error.</summary>
    public event Action<string>? Failed;

    /// <summary>Fired when the AI process starts or stops.</summary>
    public event Action<bool>? RunningChanged;

    // ── State ─────────────────────────────────────────────────────────────────

    private Process? _process;
    private System.Threading.Timer? _fakeTimer;
    private bool _disposed;
    private string[] _sources = Array.Empty<string>();
    private int _currentIndex = -1;
    private string? _fakeOutputUri;

    public bool IsRunning => (_process is { HasExited: false }) || _fakeTimer != null;

    /// <summary>True when we're in canned-demo mode (pre-rendered output instead of live AI).</summary>
    public bool IsFakeMode => _fakeTimer != null;

    /// <summary>Pre-rendered AI output URI shown in Studio when in fake mode.</summary>
    public string? FakeOutputUri => _fakeOutputUri;

    /// <summary>URI of the source the AI is currently showing as active, or null.</summary>
    public string? CurrentSourceUri =>
        (_currentIndex >= 0 && _currentIndex < _sources.Length) ? _sources[_currentIndex] : null;

    private PodcastAiService() { }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Start the AI on the given sources (file paths or RTMP URLs).
    /// If already running, this restarts with the new sources.
    /// </summary>
    public void Start(IReadOnlyList<string> sources)
    {
        if (sources is null || sources.Count == 0)
        {
            Failed?.Invoke("Aucune source fournie à l'IA.");
            return;
        }

        Stop();

        _sources      = sources.ToArray();
        // Pre-select cam 1 so the Studio tab has something to display
        // immediately, before the AI emits its first switch (~5 s on
        // first launch while models load).
        _currentIndex = _sources.Length > 0 ? 0 : -1;

        string aiHub = ResolveAiHubPath();
        if (!Directory.Exists(aiHub) ||
            !Directory.Exists(Path.Combine(aiHub, "modules", "podcast_ai")))
        {
            Failed?.Invoke(
                $"ai-hub introuvable. Cherché en remontant depuis {AppContext.BaseDirectory}.\n" +
                "Place le dossier `ai-hub` à côté de Castor-Application/ ou définis " +
                "la variable d'environnement CASTOR_AI_HUB sur le chemin du repo.");
            return;
        }

        var uv = ResolveUvPath();
        if (uv == null)
        {
            var install = OperatingSystem.IsWindows()
                ? "winget install astral-sh.uv"
                : "brew install uv";
            Failed?.Invoke($"`uv` introuvable. Installe-le ({install}) ou ajoute-le au PATH.");
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName               = uv,
            WorkingDirectory       = aiHub,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("python");
        psi.ArgumentList.Add("main.py");
        psi.ArgumentList.Add("--module");      psi.ArgumentList.Add("podcast");
        psi.ArgumentList.Add("--no-preview");
        psi.ArgumentList.Add("--json-events");
        psi.ArgumentList.Add("--sources");
        foreach (var s in sources)
            psi.ArgumentList.Add(s);

        try
        {
            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.OutputDataReceived += OnStdout;
            _process.ErrorDataReceived  += OnStderr;
            _process.Exited             += OnExited;
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            Console.Error.WriteLine($"[PodcastAI] Started (PID {_process.Id}) with {sources.Count} sources.");
            NotifyRunning(true);
        }
        catch (Exception ex)
        {
            Failed?.Invoke($"Impossible de démarrer l'IA : {ex.Message}");
            _process = null;
        }
    }

    /// <summary>
    /// Demo mode: skip the real AI and present a pre-rendered output video
    /// in Studio while cycling the multicam badges on a timer to mimic a
    /// live switcher. Used for "looks-live" presentations.
    /// </summary>
    public void StartFake(IReadOnlyList<string> sources, string fakeOutputUri, int switchPeriodMs = 3000)
    {
        if (sources is null || sources.Count == 0)
        {
            Failed?.Invoke("Aucune source fournie pour le mode démo.");
            return;
        }
        if (string.IsNullOrWhiteSpace(fakeOutputUri))
        {
            Failed?.Invoke("Aucune sortie IA pré-rendue chargée.");
            return;
        }

        Stop();

        _sources       = sources.ToArray();
        _currentIndex  = 0;
        _fakeOutputUri = fakeOutputUri;

        // Fire initial events immediately so Studio plays the pre-rendered
        // file and Multicam highlights cam 1, before the first timer tick.
        NotifyRunning(true);
        Dispatcher.UIThread.Post(() => ActiveSourceChanged?.Invoke(_currentIndex));

        _fakeTimer = new System.Threading.Timer(_ =>
        {
            if (_sources.Length == 0) return;
            _currentIndex = (_currentIndex + 1) % _sources.Length;
            Dispatcher.UIThread.Post(() => ActiveSourceChanged?.Invoke(_currentIndex));
        }, null, switchPeriodMs, switchPeriodMs);

        Console.Error.WriteLine($"[PodcastAI] Fake mode started: output={fakeOutputUri}");
    }

    /// <summary>Stop the AI process / fake timer if running.</summary>
    public void Stop()
    {
        var t = _fakeTimer;
        _fakeTimer = null;
        if (t != null)
        {
            try { t.Dispose(); } catch { /* ignore */ }
        }

        var p = _process;
        if (p != null)
        {
            try
            {
                if (!p.HasExited)
                {
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(3000);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[PodcastAI] Stop error: {ex.Message}");
            }
            finally
            {
                try { p.Dispose(); } catch { /* ignore */ }
                _process = null;
            }
        }

        bool wasRunning = t != null || p != null;
        _currentIndex  = -1;
        _fakeOutputUri = null;
        if (wasRunning) NotifyRunning(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private void OnStdout(object? sender, DataReceivedEventArgs e)
    {
        var line = e.Data;
        if (string.IsNullOrEmpty(line)) return;

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("event", out var ev)) return;
            var name = ev.GetString();
            switch (name)
            {
                case "switch":
                    if (root.TryGetProperty("index", out var idxEl) && idxEl.TryGetInt32(out var idx))
                    {
                        _currentIndex = idx;
                        Dispatcher.UIThread.Post(() => ActiveSourceChanged?.Invoke(idx));
                    }
                    break;
                case "started":
                case "stopped":
                    Console.Error.WriteLine($"[PodcastAI] {name}");
                    break;
            }
        }
        catch (JsonException)
        {
            // Non-JSON line — ai-hub shouldn't emit any when --json-events is on,
            // but be defensive against accidental prints.
            Console.Error.WriteLine($"[PodcastAI] non-json stdout: {line}");
        }
    }

    private void OnStderr(object? sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Data))
            Console.Error.WriteLine($"[ai-hub] {e.Data}");
    }

    private void OnExited(object? sender, EventArgs e)
    {
        var code = _process?.ExitCode ?? -1;
        Console.Error.WriteLine($"[PodcastAI] Process exited (code {code}).");
        Dispatcher.UIThread.Post(() => NotifyRunning(false));
        if (code != 0 && code != 137 /* SIGKILL */ && code != -1)
            Dispatcher.UIThread.Post(() => Failed?.Invoke($"L'IA s'est arrêtée (code {code})."));
    }

    private void NotifyRunning(bool running) => RunningChanged?.Invoke(running);

    /// <summary>
    /// Locate the `uv` executable. PATH may be limited when launched from an
    /// IDE, so we also probe the common install locations for each OS.
    /// </summary>
    private static string? ResolveUvPath()
    {
        var env = Environment.GetEnvironmentVariable("CASTOR_UV_PATH");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;

        var exeNames = OperatingSystem.IsWindows() ? new[] { "uv.exe", "uv" } : new[] { "uv" };

        // 1) PATH lookup.
        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(path))
        {
            foreach (var dir in path.Split(Path.PathSeparator))
            {
                foreach (var name in exeNames)
                {
                    try
                    {
                        var candidate = Path.Combine(dir, name);
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch { /* skip bad PATH entries */ }
                }
            }
        }

        // 2) Common install locations per OS.
        var home = Environment.GetEnvironmentVariable(
            OperatingSystem.IsWindows() ? "USERPROFILE" : "HOME") ?? "";
        IEnumerable<string> probes;
        if (OperatingSystem.IsWindows())
        {
            probes = new[]
            {
                Path.Combine(home, ".local", "bin", "uv.exe"),
                Path.Combine(home, ".cargo", "bin", "uv.exe"),
                @"C:\Program Files\uv\uv.exe",
            };
        }
        else
        {
            probes = new[]
            {
                "/opt/homebrew/bin/uv",        // Apple Silicon brew
                "/usr/local/bin/uv",           // Intel brew / generic
                Path.Combine(home, ".local", "bin", "uv"),
                Path.Combine(home, ".cargo", "bin", "uv"),
            };
        }
        foreach (var p in probes)
            if (File.Exists(p)) return p;

        return null;
    }

    /// <summary>
    /// Locate the ai-hub repo: env var CASTOR_AI_HUB wins, otherwise walk up
    /// from the executable looking for a sibling `ai-hub/modules/podcast_ai`.
    /// Cross-platform — uses Path.Combine and File/Directory APIs throughout,
    /// no hardcoded OS-specific fallback.
    /// </summary>
    private static string ResolveAiHubPath()
    {
        var env = Environment.GetEnvironmentVariable("CASTOR_AI_HUB");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
            return env;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            foreach (var candidate in new[]
            {
                Path.Combine(dir.FullName, "ai-hub"),
                dir.Parent != null ? Path.Combine(dir.Parent.FullName, "ai-hub") : null,
            })
            {
                if (candidate != null
                    && Directory.Exists(Path.Combine(candidate, "modules", "podcast_ai")))
                    return Path.GetFullPath(candidate);
            }
        }
        // Not found. Return an obviously-invalid sentinel so the caller's
        // Directory.Exists check fails with a clear error message instead
        // of silently using a wrong path.
        return Path.Combine(AppContext.BaseDirectory, "ai-hub-NOT-FOUND");
    }
}
