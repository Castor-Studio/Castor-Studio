using CastorApplication.Services.Diagnostics;

namespace Castor.Studio.Tests;

public sealed class CrashReportingTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 14, 30, 0, TimeSpan.FromHours(1));

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "castor-crash-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch { }
    }

    private FileLog Log(DateTimeOffset? time = null) => new(_directory, () => time ?? Now);

    private CrashReporter Reporter(int processId, Func<int, bool>? isRunning = null, DateTimeOffset? time = null) =>
        new(Log(time), processId, isRunning ?? (_ => false), () => time ?? Now);

    [Fact]
    public void A_log_line_carries_its_level_and_the_exception()
    {
        var log = Log();

        log.Write(LogLevel.Error, "Recording failed", new InvalidOperationException("disk full"));

        Assert.Equal(Path.Combine(_directory, "castor-2026-11-10.log"), log.CurrentFilePath);
        var text = File.ReadAllText(log.CurrentFilePath);
        Assert.Contains("ERROR", text);
        Assert.Contains("Recording failed", text);
        Assert.Contains("InvalidOperationException: disk full", text);
    }

    [Fact]
    public void Writing_to_an_unusable_folder_does_not_throw()
    {
        Directory.CreateDirectory(_directory);
        var blocker = Path.Combine(_directory, "not-a-folder");
        File.WriteAllText(blocker, "");

        new FileLog(blocker, () => Now).Write(LogLevel.Info, "ignored");
    }

    [Fact]
    public void Pruning_keeps_the_most_recent_files_of_each_kind()
    {
        Directory.CreateDirectory(_directory);
        for (var day = 1; day <= FileLog.RetainedFiles + 3; day++)
        {
            File.WriteAllText(Path.Combine(_directory, $"castor-2026-10-{day:00}.log"), "");
            File.WriteAllText(Path.Combine(_directory, $"crash-2026-10-{day:00}_12-00-00.txt"), "");
        }

        Log().PruneOldFiles();

        var logs = Directory.GetFiles(_directory, "castor-*.log").Select(Path.GetFileName).ToList();
        Assert.Equal(FileLog.RetainedFiles, logs.Count);
        Assert.DoesNotContain("castor-2026-10-03.log", logs);
        Assert.Contains("castor-2026-10-04.log", logs);
        Assert.Equal(FileLog.RetainedFiles, Directory.GetFiles(_directory, "crash-*.txt").Length);
    }

    [Fact]
    public void A_normal_exit_leaves_nothing_to_report()
    {
        var first = Reporter(processId: 100);
        Assert.Empty(first.BeginSession());
        first.EndSession();

        Assert.Empty(Reporter(processId: 200).BeginSession());
    }

    [Fact]
    public void A_session_that_never_ended_is_reported_without_a_report_file()
    {
        var started = Now.AddHours(-3);
        Reporter(processId: 100, time: started).BeginSession();

        var next = Reporter(processId: 200);
        var exits = next.BeginSession();
        next.EndSession();

        var exit = Assert.Single(exits);
        Assert.Equal(started, exit.StartedAt);
        Assert.Null(exit.ReportPath);
        // Reported once: the next start has nothing left to say.
        Assert.Empty(Reporter(processId: 300).BeginSession());
    }

    [Fact]
    public void A_crash_points_the_next_start_to_its_report()
    {
        var crashed = Reporter(processId: 100);
        crashed.BeginSession();

        var reportPath = crashed.ReportFatal(new NullReferenceException("boom"), "UI thread");

        Assert.NotNull(reportPath);
        var report = File.ReadAllText(reportPath);
        Assert.Contains("NullReferenceException: boom", report);
        Assert.Contains("Origin: UI thread", report);
        Assert.Contains($"Version: {AppInfo.Version}", report);

        var exit = Assert.Single(Reporter(processId: 200).BeginSession());
        Assert.Equal(reportPath, exit.ReportPath);
    }

    [Fact]
    public void Only_the_first_fatal_exception_is_reported()
    {
        var reporter = Reporter(processId: 100);
        reporter.BeginSession();

        Assert.NotNull(reporter.ReportFatal(new Exception("first"), "UI thread"));
        Assert.Null(reporter.ReportFatal(new Exception("second"), "AppDomain"));

        Assert.Single(Directory.GetFiles(_directory, "crash-*.txt"));
    }

    [Fact]
    public void Another_instance_still_running_is_not_a_crash()
    {
        Reporter(processId: 100).BeginSession();

        var second = Reporter(processId: 200, isRunning: id => id == 100);
        Assert.Empty(second.BeginSession());
        second.EndSession();
        // Its session file stays, for when it really ends.
        Assert.Single(Reporter(processId: 300).BeginSession());
    }
}
