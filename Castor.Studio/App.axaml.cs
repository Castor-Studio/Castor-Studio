using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CastorApplication.ViewModels.Shell;
using CastorApplication.Services.Ai;
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

            var collection = new ServiceCollection();
            collection.AddCommonServices(desktop);
            _services = collection.BuildServiceProvider();
            desktop.MainWindow = new MainWindow { DataContext = _services.GetRequiredService<MainViewModel>() };
            desktop.Exit += (_, _) =>
            {
                _services.GetRequiredService<StudioDockViewModel>().SaveLayout();
                ShutdownServicesAsync(_services).GetAwaiter().GetResult();
                _services = null;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task ShutdownServicesAsync(ServiceProvider services)
    {
        // Keep this order: the AI session owns the auxiliary outputs, and libobs must
        // not be shut down while recording or an AI output still holds native handles.
        try { await services.GetRequiredService<IAiAnalysisClient>().StopSessionAsync("desktop_exit", CancellationToken.None); }
        catch { }
        try { await services.GetRequiredService<IAiSceneStreamRuntime>().StopAllAsync(CancellationToken.None); }
        catch { }

        var workspace = services.GetRequiredService<ViewModels.Studio.StudioWorkspaceViewModel>();
        if (workspace.IsRecording)
        {
            try { await services.GetRequiredService<IRecordingRuntime>().StopRecordingAsync(CancellationToken.None); }
            catch { }
        }
        if (workspace.IsStreaming)
        {
            try { await services.GetRequiredService<IStreamingRuntime>().StopStreamingAsync(CancellationToken.None); }
            catch { }
        }

        try { await services.GetRequiredService<IAiAnalysisClient>().DisposeAsync(); }
        catch { }
        services.Dispose();
    }
}
