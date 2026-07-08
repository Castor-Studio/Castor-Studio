using System.Net.Http;
using Castor.Engine.Services;
using CastorApplication.Services;
using CastorApplication.Services.Ai;
using CastorApplication.Services.Auth;
using CastorApplication.Services.Auth.Providers;
using CastorApplication.Services.Auth.Providers.Twitch;
using CastorApplication.Services.Auth.Storage;
using CastorApplication.Services.Config;
using CastorApplication.Services.Settings;
using CastorApplication.ViewModels;
using CastorApplication.ViewModels.Settings;
using CastorApplication.ViewModels.Settings.Sections;
using Microsoft.Extensions.DependencyInjection;

namespace CastorApplication;

public static class ServiceCollectionExtensions
{
    public static void AddCommonServices(this IServiceCollection collection)
    {
        collection.AddCastorEngine();

        collection.AddSingleton<HttpClient>();

        collection.AddSingleton<ITokenStore, InMemoryTokenStore>();
        collection.AddSingleton<IProviderStore, ProviderStore>();

        collection.AddSingleton<IConfigService, JsonConfigService>();

        collection.AddSingleton<IAuthProvider, TwitchAuthProvider>();

        collection.AddSingleton<IAuthService, AuthService>();
        collection.AddSingleton<ProviderRegistry>();

        collection.AddSingleton<SettingsService>();
        collection.AddSingleton<IFilePickerService, AvaloniaFilePickerService>();
        collection.AddSingleton<IThemeService, AvaloniaThemeService>();
        collection.AddSingleton<IAiAnalysisClient, GrpcAiAnalysisClient>();

        collection.AddTransient<MainViewModel>();

        collection.AddTransient<StudioViewModel>();
        collection.AddTransient<MulticamViewModel>();
        collection.AddTransient<ScenesViewModel>();
        collection.AddTransient<SettingsViewModel>();

        collection.AddTransient<GeneralSettingsViewModel>();
        collection.AddTransient<VideoSettingsViewModel>();
        collection.AddTransient<AudioSettingsViewModel>();
        collection.AddTransient<StreamingSettingsViewModel>();
        collection.AddTransient<OutputSettingsViewModel>();
        collection.AddTransient<AccountsSettingsViewModel>();
    }
}
