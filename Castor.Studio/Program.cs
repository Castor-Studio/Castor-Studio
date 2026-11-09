using System;
using System.Runtime.InteropServices;
using Avalonia;
using CastorApplication.Services.Diagnostics;

namespace CastorApplication
{
    internal sealed class Program
    {
        private static CrashReporter? _crashes;

        // Runs that ended abnormally before this one, most recent first: App tells the user once
        // the main window is up.
        internal static IReadOnlyList<UncleanExit> UncleanExits { get; private set; } = [];

        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static int Main(string[] args)
        {
            var log = new FileLog(FileLog.DefaultDirectory);
            AppLog.Initialize(log);
            var crashes = _crashes = new CrashReporter(log);

            // Exceptions on any other thread end here, just before the process goes down.
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                crashes.ReportFatal(e.ExceptionObject as Exception
                    ?? new Exception(e.ExceptionObject?.ToString()), "AppDomain");
            // A faulted task nobody awaited: .NET no longer crashes on it, but it is a bug.
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                AppLog.Error("Unobserved task exception", e.Exception);
                e.SetObserved();
            };

            UncleanExits = crashes.BeginSession();
            log.PruneOldFiles();
            AppLog.Info($"Castor Studio {AppInfo.Version} starting on {RuntimeInformation.OSDescription}, "
                + $"{RuntimeInformation.FrameworkDescription}");

            int exitCode;
            try
            {
                exitCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception exception)
            {
                // Thrown on the UI thread and never handled: the dispatcher loop gives up.
                crashes.ReportFatal(exception, "UI thread");
                throw;
            }

            AppLog.Info($"Castor Studio exited normally (code {exitCode})");
            crashes.EndSession();
            return exitCode;
        }

        // The user's work is safe from here on: a crash after this is not reported at next start.
        internal static void MarkShutdownReached()
        {
            AppLog.Info("Sessions stopped, shutdown reached");
            _crashes?.EndSession();
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
    }
}
