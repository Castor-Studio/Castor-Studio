using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Castor.Native;
using CastorApplication.Services;
using CastorApplication.ViewModels;
using CastorApplication.Views;
using System;

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
            CastorNative.Initialize();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
                // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
                DisableAvaloniaDataAnnotationValidation();
                desktop.MainWindow = new MainWindow { DataContext = new MainWindowViewModel() };

                // Arrêt propre des threads natifs avant la fermeture de l'app
                desktop.ShutdownRequested += (_, _) => RecorderService.Instance.Stop();
                AppDomain.CurrentDomain.ProcessExit += (_, _) => RecorderService.Instance.Stop();
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void DisableAvaloniaDataAnnotationValidation()
        {
            // Get an array of plugins to remove
            var dataValidationPluginsToRemove = BindingPlugins
                .DataValidators.OfType<DataAnnotationsValidationPlugin>()
                .ToArray();

            // remove each entry found
            foreach (var plugin in dataValidationPluginsToRemove)
            {
                BindingPlugins.DataValidators.Remove(plugin);
            }
        }
    }
}
