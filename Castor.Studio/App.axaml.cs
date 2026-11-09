using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CastorApplication.ViewModels.Shell;
using CastorApplication.Services.Ai;
using CastorApplication.Services.Diagnostics;
using CastorApplication.Services.Dialogs;
using CastorApplication.Services.Studio;
using CastorApplication.Views;
using Dock.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace CastorApplication;

public partial class App : Application
{
    private ServiceProvider? _services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // A detached panel is a window in its own right: no owner window, so it gets its own
            // taskbar entry, can go behind the main window and can be moved to another screen.
            // Closing the main window still takes them down, rather than leaving stray windows
            // behind that would keep the process alive.
            DockSettings.FloatingWindowOwnerPolicy = DockFloatingWindowOwnerPolicy.NeverOwned;
            CastorApplication.Docking.StudioPanelChrome.Register();
            DockSettings.CloseFloatingWindowsOnMainWindowClose = true;

            // And whatever Dock misses - a detached window it no longer tracks, one gone off
            // screen - must not keep the process alive with no main window left. The default,
            // OnLastWindowClose, would: Castor kept running invisibly, holding the exe.
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            var collection = new ServiceCollection();
            collection.AddCommonServices(desktop);
            _services = collection.BuildServiceProvider();
            desktop.MainWindow = new MainWindow { DataContext = _services.GetRequiredService<MainViewModel>() };
            ShowCrashNoticeOnceOpened(desktop.MainWindow, _services.GetRequiredService<ICrashNoticeDialogService>());
            desktop.Exit += (_, _) =>
            {
                AppLog.Info("Shutting down");
                _services.GetRequiredService<StudioDockViewModel>().SaveLayout();
                // Off the UI thread: blocking it here while the shutdown awaits a continuation
                // posted back to it (stopping the AI worker does) would never return.
                var services = _services;
                Task.Run(() => ShutdownServicesAsync(services)).GetAwaiter().GetResult();
                _services = null;
                AppLog.Info("Services stopped");
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Only the most recent unclean exit: older ones are in the log, and a stack of dialogs at
    // startup would say nothing more.
    private static void ShowCrashNoticeOnceOpened(Window mainWindow, ICrashNoticeDialogService notices)
    {
        if (Program.UncleanExits is not [var latest, ..]) return;

        async void OnOpened(object? sender, EventArgs e)
        {
            mainWindow.Opened -= OnOpened;
            try { await notices.ShowAsync(latest); }
            catch (Exception exception) { AppLog.Warning("Could not show the crash notice", exception); }
        }

        mainWindow.Opened += OnOpened;
    }

    private static async Task ShutdownServicesAsync(ServiceProvider services)
    {
        // Keep this order: the AI session owns the scene renders it reads, and libobs must
        // not be shut down while recording or an AI render still holds native handles.
        try { await services.GetRequiredService<IAiAnalysisClient>().StopSessionAsync("desktop_exit", CancellationToken.None); }
        catch (Exception exception) { AppLog.Warning("Stopping the AI session on exit failed", exception); }

        var workspace = services.GetRequiredService<ViewModels.Studio.StudioWorkspaceViewModel>();
        if (workspace.IsRecording)
        {
            try { await services.GetRequiredService<IRecordingRuntime>().StopRecordingAsync(CancellationToken.None); }
            catch (Exception exception) { AppLog.Error("Stopping the recording on exit failed", exception); }
        }
        if (workspace.IsStreaming)
        {
            try { await services.GetRequiredService<IStreamingRuntime>().StopStreamingAsync(CancellationToken.None); }
            catch (Exception exception) { AppLog.Error("Stopping the stream on exit failed", exception); }
        }

        try { await services.GetRequiredService<IAiAnalysisClient>().DisposeAsync(); }
        catch (Exception exception) { AppLog.Warning("Disposing the AI client on exit failed", exception); }

        // Recordings are finalized and lives ended: nothing the user made can be lost past this
        // point. libobs currently crashes inside Obs.Shutdown (access violation in obs.dll) on
        // every exit; without this, every normal close would be reported as a crash at the next
        // start. The log still shows "libobs shutting down" with no "libobs shut down" after it.
        Program.MarkShutdownReached();

        // AiClient (Castor.AI) is only IAsyncDisposable: a synchronous Dispose would throw.
        await services.DisposeAsync();
    }
}
