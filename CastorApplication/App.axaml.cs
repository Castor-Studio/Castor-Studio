using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Castor.Native;
using CastorApplication.Services;
using CastorApplication.ViewModels;
using CastorApplication.Views;
using CastorApplication.Services.Auth.Providers;

namespace CastorApplication
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // If you use CommunityToolkit, line below is needed to remove Avalonia data validation.
            // Without this line you will get duplicate validations from both Avalonia and CT
            BindingPlugins.DataValidators.RemoveAt(0);

            // Register all the services needed for the application to run
            var collection = new ServiceCollection();
            collection.AddCommonServices();

            // Creates a ServiceProvider containing services from the provided IServiceCollection
            var services = collection.BuildServiceProvider();

            CastorNative.Initialize();
            MediaMtxService.Instance.Start();

            var vm = services.GetRequiredService<MainViewModel>();
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = vm
                };

                // Arrêt propre des threads natifs et de MediaMTX avant la fermeture de l'app
                desktop.ShutdownRequested += (_, _) =>
                {
                    RecorderService.Instance.Stop();
                    MediaMtxService.Instance.Stop();
                };
                AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                {
                    RecorderService.Instance.Stop();
                    MediaMtxService.Instance.Stop();
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
