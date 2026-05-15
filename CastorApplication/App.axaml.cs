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
            // The native libcastor + bundled mediamtx.exe are Windows-only.
            // On macOS/Linux we run a reduced feature set (no recording/streaming
            // via the native pipeline) — the Multicam page still works via the
            // ai-hub subprocess, which is what the demo needs.
            if (OperatingSystem.IsWindows())
            {
                try { CastorNative.Initialize(); }
                catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine($"[CastorNative] init failed: {ex.Message}"); }

                try { MediaMtxService.Instance.Start(); }
                catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine($"[MediaMTX] start failed: {ex.Message}"); }
            }

            // Touch the multicam sources singleton so its AI-event subscription
            // is wired before any view is shown.
            _ = MulticamSourcesService.Instance;

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
                // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
                DisableAvaloniaDataAnnotationValidation();
                desktop.MainWindow = new MainWindow { DataContext = new MainWindowViewModel() };

                // Arrêt propre des threads natifs et de MediaMTX avant la fermeture de l'app
                desktop.ShutdownRequested += (_, _) =>
                {
                    PodcastAiService.Instance.Stop();
                    if (OperatingSystem.IsWindows())
                    {
                        try { RecorderService.Instance.Stop(); } catch { /* ignore */ }
                        try { MediaMtxService.Instance.Stop(); } catch { /* ignore */ }
                    }
                };
                AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                {
                    PodcastAiService.Instance.Stop();
                    if (OperatingSystem.IsWindows())
                    {
                        try { RecorderService.Instance.Stop(); } catch { /* ignore */ }
                        try { MediaMtxService.Instance.Stop(); } catch { /* ignore */ }
                    }
                };
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
