using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Castor.AI;
using Castor.AI.Providers;
using Castor.AI.Providers.Local;
using CastorApplication.Models.Config;
using CastorApplication.Services;
using CastorApplication.Services.Ai;
using CastorApplication.Services.Auth;
using CastorApplication.Services.Auth.Providers;
using CastorApplication.Services.Auth.Providers.Twitch;
using CastorApplication.Services.Auth.Storage;
using CastorApplication.Services.Config;
using CastorApplication.Services.Dialogs;
using CastorApplication.Services.Platform;
using CastorApplication.Services.Settings;
using CastorApplication.Services.Studio;
using CastorApplication.ViewModels.Multicam;
using CastorApplication.ViewModels.Scenes;
using CastorApplication.ViewModels.Settings;
using CastorApplication.ViewModels.Settings.Sections;
using CastorApplication.ViewModels.Shell;
using CastorApplication.ViewModels.Studio;
using Microsoft.Extensions.DependencyInjection;

namespace CastorApplication;

public static class ServiceCollectionExtensions
{
    public static void AddCommonServices(this IServiceCollection services, IClassicDesktopStyleApplicationLifetime desktop)
    {
        services.AddSingleton(desktop);

        services.AddSingleton<HttpClient>();
        services.AddSingleton<ITokenStore, InMemoryTokenStore>();
        services.AddSingleton<IProviderStore, ProviderStore>();
        services.AddSingleton<IConfigService, JsonConfigService>();
        services.AddSingleton<IAuthProvider, TwitchAuthProvider>();
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<ProviderRegistry>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<IPrimaryMonitorResolutionProvider, WindowsPrimaryMonitorResolutionProvider>();
        services.AddSingleton<VideoCanvasResolutionResolver>();
        services.AddSingleton<IFilePickerService, AvaloniaFilePickerService>();
        services.AddSingleton<IThemeService, AvaloniaThemeService>();
        services.AddSingleton<IDockChromeService, AvaloniaDockChromeService>();
        services.AddSingleton<DockLayoutService>();
        services.AddSingleton<StatusMessageService>();

        services.AddSingleton<IStudioRuntime, UnavailableStudioRuntime>();

        services.AddSingleton(provider =>
            new LibObsSceneRuntime(
                provider.GetRequiredService<SettingsService>(),
                provider.GetRequiredService<VideoCanvasResolutionResolver>()
        ));

        services.AddSingleton<ISceneRuntime>(provider => provider.GetRequiredService<LibObsSceneRuntime>());
        services.AddSingleton<ISourceRuntime>(provider => provider.GetRequiredService<LibObsSceneRuntime>());
        services.AddSingleton<IAiSceneSourceProvider>(provider => provider.GetRequiredService<LibObsSceneRuntime>());
        services.AddSingleton<IRecordingRuntime>(provider => provider.GetRequiredService<LibObsSceneRuntime>());
        services.AddSingleton<IStreamingRuntime>(provider => provider.GetRequiredService<LibObsSceneRuntime>());
        services.AddSingleton<IScenePreviewRuntime>(provider => provider.GetRequiredService<LibObsSceneRuntime>());
        services.AddSingleton<ISceneCollectionService, SceneCollectionService>();
        AddLocalAi(services);
        services.AddSingleton<IAiZoomEntryPoint, AiZoomEntryPoint>();
        services.AddSingleton<IAddSourceDialogService, AddSourceDialogService>();
        services.AddSingleton<ISceneTransferDialogService, SceneTransferDialogService>();
        services.AddSingleton<ICancelRecordingDialogService, CancelRecordingDialogService>();
        services.AddSingleton<IUnsavedScenesDialogService, UnsavedScenesDialogService>();
        services.AddSingleton<IRtmpDiscoveryService, RtmpDiscoveryService>();
        services.AddSingleton<IAddSourceDialogViewModelFactory, AddSourceDialogViewModelFactory>();
        services.AddSingleton<StudioWorkspaceViewModel>();

        services.AddSingleton<GeneralSettingsViewModel>();
        services.AddSingleton<VideoSettingsViewModel>(provider =>
            new VideoSettingsViewModel(provider.GetRequiredService<VideoCanvasResolutionResolver>()));
        services.AddSingleton<AudioSettingsViewModel>();
        services.AddSingleton<StreamingSettingsViewModel>();
        services.AddSingleton<OutputSettingsViewModel>();
        services.AddSingleton<AccountsSettingsViewModel>();
        services.AddSingleton<SettingsViewModel>();

        services.AddSingleton(provider => new StudioViewModel(
            provider.GetRequiredService<StudioWorkspaceViewModel>(),
            provider.GetRequiredService<IStudioRuntime>(),
            provider.GetRequiredService<IScenePreviewRuntime>(),
            provider.GetRequiredService<IRecordingRuntime>(),
            provider.GetRequiredService<IStreamingRuntime>(),
            provider.GetRequiredService<IProviderStore>(),
            provider.GetRequiredService<SettingsService>(),
            provider.GetRequiredService<VideoCanvasResolutionResolver>(),
            provider.GetRequiredService<StatusMessageService>(),
            cancelRecordingDialog: provider.GetRequiredService<ICancelRecordingDialogService>()
        ));

        services.AddSingleton(provider => new ScenesViewModel(
            provider.GetRequiredService<StudioWorkspaceViewModel>(),
            provider.GetRequiredService<IStudioRuntime>(),
            provider.GetRequiredService<IScenePreviewRuntime>(),
            provider.GetRequiredService<ISceneRuntime>(),
            provider.GetRequiredService<ISourceRuntime>(),
            provider.GetRequiredService<IFilePickerService>(),
            provider.GetRequiredService<ISceneCollectionService>(),
            provider.GetRequiredService<IAddSourceDialogViewModelFactory>(),
            provider.GetRequiredService<IAddSourceDialogService>(),
            provider.GetRequiredService<ISceneTransferDialogService>(),
            provider.GetRequiredService<SettingsService>(),
            provider.GetRequiredService<VideoCanvasResolutionResolver>(),
            provider.GetRequiredService<StatusMessageService>(),
            SceneCollectionService.DefaultCollectionPath
        ));

        services.AddSingleton(provider => new MulticamViewModel(
            provider.GetRequiredService<IAiAnalysisClient>(),
            provider.GetRequiredService<StudioWorkspaceViewModel>(),
            provider.GetRequiredService<IScenePreviewRuntime>(),
            provider.GetRequiredService<SettingsService>()
        ));
        services.AddSingleton(provider => new StudioDockViewModel(
            provider.GetRequiredService<StudioViewModel>(),
            provider.GetRequiredService<DockLayoutService>()
        ));

        services.AddSingleton(provider => new MainViewModel(
            provider.GetRequiredService<StudioViewModel>(),
            provider.GetRequiredService<StudioDockViewModel>(),
            provider.GetRequiredService<MulticamViewModel>(),
            provider.GetRequiredService<ScenesViewModel>(),
            provider.GetRequiredService<SettingsViewModel>(),
            provider.GetRequiredService<StudioWorkspaceViewModel>(),
            provider.GetRequiredService<IClassicDesktopStyleApplicationLifetime>(),
            provider.GetRequiredService<StatusMessageService>(),
            provider.GetRequiredService<IUnsavedScenesDialogService>()
        ));
    }

    private static void AddLocalAi(IServiceCollection services)
    {
        services.AddSingleton(provider => new AiClient(
            CreateAiProvider(
                provider.GetRequiredService<IConfigService>().Config.AiWorker,
                provider.GetRequiredService<HttpClient>()),
            new AiClientOptions { Dispatch = action => Dispatcher.UIThread.Post(action) }));

        services.AddSingleton<IAiSceneFrameRuntime>(provider =>
            new LibObsAiSceneFrameRuntime(provider.GetRequiredService<IAiSceneSourceProvider>()));
        services.AddSingleton<IAiAudioCaptureRuntime>(provider =>
            new LibObsAiAudioCaptureRuntime(provider.GetRequiredService<IAiSceneSourceProvider>()));
        services.AddSingleton<IAiAnalysisClient>(provider => new LocalAiAnalysisClient(
            provider.GetRequiredService<AiClient>(),
            provider.GetRequiredService<IAiSceneFrameRuntime>(),
            provider.GetRequiredService<IAiAudioCaptureRuntime>()));
    }

    private static IAiProvider CreateAiProvider(AiWorkerConfig config, HttpClient http)
    {
        LocalWorkerProvider CreateWorker(string? path) => new(new LocalWorkerOptions
        {
            WorkerPath = path,
            Device = string.Equals(config.Device?.Trim(), "cpu", StringComparison.OrdinalIgnoreCase) ? "cpu" : "gpu",
            GpuAdapter = Math.Max(0, config.GpuAdapter),
        });

        // Dev override: the SDK reads CASTOR_WORKER_PATH when it gets no path.
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CASTOR_WORKER_PATH")))
            return CreateWorker(null);

        return new InstallingWorkerProvider(new AiWorkerInstaller(http), CreateWorker);
    }
}
