using System.Text;

namespace CastorApplication.Services.Diagnostics;

public enum LogLevel
{
    Info,
    Warning,
    Error,
    Fatal
}

// One text file per day under %LOCALAPPDATA%\Castor\logs, which is what a beta tester attaches
// to a bug report. Writing never throws: a full disk or a locked file must not turn a log line
// into a second failure.
public sealed class FileLog
{
    // Two weeks of days the app ran, and as many crash reports.
    public const int RetainedFiles = 14;

    private const string LogPrefix = "castor-";
    private const string LogExtension = ".log";

    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _clock;

    public FileLog(string directory, Func<DateTimeOffset>? clock = null)
    {
        Directory = directory;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Castor", "logs");

    public string Directory { get; }

    public string CurrentFilePath => FilePathFor(_clock());

    public void Write(LogLevel level, string message, Exception? exception = null)
    {
        try
        {
            var now = _clock();
            var entry = new StringBuilder()
                .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
                .Append(' ')
                .Append(Label(level))
                .Append(" [")
                .Append(Environment.CurrentManagedThreadId)
                .Append("] ")
                .Append(message)
                .AppendLine();
            if (exception != null) entry.Append(exception).AppendLine();

            lock (_gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.AppendAllText(FilePathFor(now), entry.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Nowhere left to report it.
        }
    }

    // Keeps the most recent files of each kind, oldest first out.
    public void PruneOldFiles()
    {
        try
        {
            if (!System.IO.Directory.Exists(Directory)) return;
            Prune($"{LogPrefix}*{LogExtension}");
            Prune($"{CrashReporter.ReportPrefix}*{CrashReporter.ReportExtension}");
        }
        catch
        {
            // Old files staying behind is harmless.
        }
    }

    private void Prune(string pattern)
    {
        // The names carry the date, so their order is the files' age.
        var stale = System.IO.Directory.GetFiles(Directory, pattern)
            .OrderByDescending(path => Path.GetFileName(path), StringComparer.Ordinal)
            .Skip(RetainedFiles);
        foreach (var path in stale)
        {
            try { File.Delete(path); }
            catch { }
        }
    }

    private string FilePathFor(DateTimeOffset time) =>
        Path.Combine(Directory, $"{LogPrefix}{time:yyyy-MM-dd}{LogExtension}");

    private static string Label(LogLevel level) => level switch
    {
        LogLevel.Info => "INFO ",
        LogLevel.Warning => "WARN ",
        LogLevel.Error => "ERROR",
        _ => "FATAL"
    };
}

// The app-wide entry point, usable from anywhere, before the service container exists and
// after it is gone. Does nothing until Program sets it up, so tests write no files.
public static class AppLog
{
    private static FileLog? _log;

    public static string Directory => _log?.Directory ?? FileLog.DefaultDirectory;

    public static void Initialize(FileLog log) => _log = log;

    public static void Info(string message) => _log?.Write(LogLevel.Info, message);

    public static void Warning(string message, Exception? exception = null) =>
        _log?.Write(LogLevel.Warning, message, exception);

    public static void Error(string message, Exception? exception = null) =>
        _log?.Write(LogLevel.Error, message, exception);
}
