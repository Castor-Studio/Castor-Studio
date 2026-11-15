using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace CastorApplication.Services.Diagnostics;

// A previous run that did not reach the end of Program.Main. ReportPath is set when a managed
// exception took it down; null means something the runtime could not catch: a native crash in
// libobs, the process killed from the task manager, the machine switched off.
public sealed record UncleanExit(DateTimeOffset StartedAt, string? ReportPath);

// Tells a crash apart from a normal exit across runs. Each run leaves a session file next to the
// logs and deletes it on a normal exit: one still there at the next start belonged to a run that
// never got to close. A file per process, so two instances running side by side do not take
// each other for a crash.
public sealed class CrashReporter
{
    public const string ReportPrefix = "crash-";
    public const string ReportExtension = ".txt";

    private const string SessionPrefix = "session-";
    private const string SessionExtension = ".lock";
    private const string ReportKey = "report=";

    private readonly FileLog _log;
    private readonly int _processId;
    private readonly Func<int, bool> _isCastorRunning;
    private readonly Func<DateTimeOffset> _clock;
    private int _reported;

    public CrashReporter(
        FileLog log,
        int? processId = null,
        Func<int, bool>? isCastorRunning = null,
        Func<DateTimeOffset>? clock = null)
    {
        _log = log;
        _processId = processId ?? Environment.ProcessId;
        _isCastorRunning = isCastorRunning ?? IsCastorProcessRunning;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    private string SessionPath => Path.Combine(_log.Directory, $"{SessionPrefix}{_processId}{SessionExtension}");

    // Collects the runs that ended abnormally, most recent first, then marks this one as started.
    public IReadOnlyList<UncleanExit> BeginSession()
    {
        var uncleanExits = new List<UncleanExit>();
        try
        {
            Directory.CreateDirectory(_log.Directory);
            foreach (var path in Directory.GetFiles(_log.Directory, $"{SessionPrefix}*{SessionExtension}"))
            {
                if (!TryReadProcessId(path, out var processId) || processId == _processId) continue;
                if (_isCastorRunning(processId)) continue;

                uncleanExits.Add(ReadSession(path));
                TryDelete(path);
            }

            File.WriteAllText(SessionPath,
                $"{_clock().ToString("O", CultureInfo.InvariantCulture)}{Environment.NewLine}", Encoding.UTF8);
        }
        catch (Exception exception)
        {
            _log.Write(LogLevel.Warning, "Could not check how the previous session ended", exception);
        }

        foreach (var exit in uncleanExits)
            _log.Write(LogLevel.Warning, exit.ReportPath == null
                ? $"Session started at {exit.StartedAt:O} did not exit normally (native crash or killed)"
                : $"Session started at {exit.StartedAt:O} crashed, report: {exit.ReportPath}");

        return uncleanExits.OrderByDescending(exit => exit.StartedAt).ToList();
    }

    public void EndSession() => TryDelete(SessionPath);

    // Writes the report of the exception that is taking the process down. Only the first one
    // counts: the UI thread's exception reaches both Program.Main and the AppDomain handler.
    public string? ReportFatal(Exception exception, string origin)
    {
        if (Interlocked.Exchange(ref _reported, 1) == 1) return null;

        _log.Write(LogLevel.Fatal, $"Unhandled exception ({origin})", exception);
        try
        {
            var now = _clock();
            var path = Path.Combine(_log.Directory,
                $"{ReportPrefix}{now:yyyy-MM-dd_HH-mm-ss}{ReportExtension}");
            var report = new StringBuilder()
                .AppendLine("Castor Studio crash report")
                .AppendLine($"Time: {now:O}")
                .AppendLine($"Version: {AppInfo.Version}")
                .AppendLine($"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})")
                .AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}")
                .AppendLine($"Origin: {origin}")
                .AppendLine($"Log: {_log.CurrentFilePath}")
                .AppendLine()
                .Append(exception)
                .AppendLine();

            Directory.CreateDirectory(_log.Directory);
            File.WriteAllText(path, report.ToString(), Encoding.UTF8);
            File.AppendAllText(SessionPath, $"{ReportKey}{path}{Environment.NewLine}", Encoding.UTF8);
            return path;
        }
        catch
        {
            // The log line above is all that is left.
            return null;
        }
    }

    private static UncleanExit ReadSession(string path)
    {
        var lines = File.ReadAllLines(path);
        var startedAt = lines.Length > 0
            && DateTimeOffset.TryParse(lines[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : new DateTimeOffset(File.GetLastWriteTime(path));
        var report = lines
            .Where(line => line.StartsWith(ReportKey, StringComparison.Ordinal))
            .Select(line => line[ReportKey.Length..])
            .LastOrDefault(File.Exists);
        return new UncleanExit(startedAt, report);
    }

    private static bool TryReadProcessId(string path, out int processId)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return int.TryParse(name[SessionPrefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out processId);
    }

    // The id alone is not enough: Windows hands out ids again, to any program.
    private static bool IsCastorProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            using var current = Process.GetCurrentProcess();
            return !process.HasExited
                && string.Equals(process.ProcessName, current.ProcessName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { }
    }
}
