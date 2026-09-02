using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CastorApplication.ViewModels.Shell;
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
            DockSettings.CloseFloatingWindowsOnMainWindowClose = true;

            var collection = new ServiceCollection();
            collection.AddCommonServices(desktop);
            _services = collection.BuildServiceProvider();
            desktop.MainWindow = new MainWindow { DataContext = _services.GetRequiredService<MainViewModel>() };
            desktop.Exit += (_, _) =>
            {
                _services.GetRequiredService<StudioDockViewModel>().SaveLayout();
                _services.Dispose();
                _services = null;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
