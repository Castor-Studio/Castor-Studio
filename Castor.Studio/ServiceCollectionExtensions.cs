using Avalonia.Controls.ApplicationLifetimes;
using CastorApplication.Services;
using CastorApplication.Services.Ai;
using CastorApplication.Services.Auth;
using CastorApplication.Services.Auth.Providers;
using CastorApplication.Services.Auth.Providers.Twitch;
using CastorApplication.Services.Auth.Storage;
using CastorApplication.Services.Config;
using CastorApplication.Services.Dialogs;
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
        services.AddSingleton<IFilePickerService, AvaloniaFilePickerService>();
        services.AddSingleton<IThemeService, AvaloniaThemeService>();
        services.AddSingleton<IDockChromeService, AvaloniaDockChromeService>();
        services.AddSingleton<DockLayoutService>();

        services.AddSingleton<IStudioRuntime, UnavailableStudioRuntime>();
        services.AddSingleton<LibObsSceneRuntime>();
        services.AddSingleton<ISceneRuntime>(provider => provider.GetRequiredService<LibObsSceneRuntime>());
        services.AddSingleton<ISourceRuntime>(provider => provider.GetRequiredService<LibObsSceneRuntime>());
        services.AddSingleton<ISceneCollectionService, SceneCollectionService>();
        services.AddSingleton<IAiAnalysisClient, UnavailableAiAnalysisClient>();
        services.AddSingleton<IAddSourceDialogService, AddSourceDialogService>();
        services.AddSingleton<IAddSourceDialogViewModelFactory, AddSourceDialogViewModelFactory>();
        services.AddSingleton<StudioWorkspaceViewModel>();

        services.AddSingleton<GeneralSettingsViewModel>();
        services.AddSingleton<VideoSettingsViewModel>();
        services.AddSingleton<AudioSettingsViewModel>();
        services.AddSingleton<StreamingSettingsViewModel>();
        services.AddSingleton<OutputSettingsViewModel>();
        services.AddSingleton<AccountsSettingsViewModel>();
        services.AddSingleton<SettingsViewModel>();

        services.AddSingleton(provider => new StudioViewModel(
            provider.GetRequiredService<StudioWorkspaceViewModel>(), provider.GetRequiredService<IStudioRuntime>(),
            provider.GetRequiredService<IProviderStore>(), provider.GetRequiredService<SettingsService>(),
            provider.GetRequiredService<IFilePickerService>()));
        services.AddSingleton(provider => new ScenesViewModel(
            provider.GetRequiredService<StudioWorkspaceViewModel>(), provider.GetRequiredService<IStudioRuntime>(),
            provider.GetRequiredService<ISceneRuntime>(), provider.GetRequiredService<ISourceRuntime>(),
            provider.GetRequiredService<IFilePickerService>(), provider.GetRequiredService<ISceneCollectionService>(),
            provider.GetRequiredService<IAddSourceDialogViewModelFactory>(), provider.GetRequiredService<IAddSourceDialogService>()));
        services.AddSingleton(provider => new MulticamViewModel(
            provider.GetRequiredService<IAiAnalysisClient>(), provider.GetRequiredService<StudioWorkspaceViewModel>()));
        services.AddSingleton(provider => new StudioDockViewModel(
            provider.GetRequiredService<StudioViewModel>(), provider.GetRequiredService<DockLayoutService>()));
        services.AddSingleton(provider => new MainViewModel(
            provider.GetRequiredService<StudioViewModel>(), provider.GetRequiredService<StudioDockViewModel>(),
            provider.GetRequiredService<MulticamViewModel>(), provider.GetRequiredService<ScenesViewModel>(),
            provider.GetRequiredService<SettingsViewModel>(), provider.GetRequiredService<StudioWorkspaceViewModel>(),
            provider.GetRequiredService<IClassicDesktopStyleApplicationLifetime>()));
    }
}
