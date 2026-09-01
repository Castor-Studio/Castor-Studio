using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CastorApplication.ViewModels.Shell;
using CastorApplication.Views;
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
            var collection = new ServiceCollection();
            collection.AddCommonServices(desktop);
            _services = collection.BuildServiceProvider();
            desktop.MainWindow = new MainWindow { DataContext = _services.GetRequiredService<MainViewModel>() };
            desktop.Exit += (_, _) =>
            {
                _services.Dispose();
                _services = null;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
