using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using CastorApplication.Services.Auth;
using CastorApplication.Services.Auth.Providers;
using CastorApplication.Services.Auth.Providers.Twitch;
using CastorApplication.Services.Auth.Storage;
using CastorApplication.Services.Config;
using CastorApplication.Services.Settings;
using CastorApplication.ViewModels;
using CastorApplication.ViewModels.Settings;
using CastorApplication.ViewModels.Settings.Sections;

namespace CastorApplication
{
    public static class ServiceCollectionExtensions
    {
        public static void AddCommonServices(this IServiceCollection collection)
        {
            collection.AddSingleton<HttpClient>();

            collection.AddSingleton<ITokenStore, InMemoryTokenStore>();
            collection.AddSingleton<IProviderStore, ProviderStore>();

            collection.AddSingleton<IConfigService, JsonConfigService>();

            collection.AddSingleton<IAuthProvider, TwitchAuthProvider>();

            collection.AddSingleton<IAuthService, AuthService>();
            collection.AddSingleton<ProviderRegistry>();

            collection.AddSingleton<SettingsService>();

            // ViewModels

            collection.AddTransient<MainViewModel>();

            //// Main Pages
            collection.AddTransient<StudioViewModel>();
            collection.AddTransient<MulticamViewModel>();
            collection.AddTransient<ScenesViewModel>();
            collection.AddTransient<SettingsViewModel>();

            ////// Setting Sections
            collection.AddTransient<GeneralSettingsViewModel>();
            collection.AddTransient<VideoSettingsViewModel>();
            collection.AddTransient<AudioSettingsViewModel>();
            collection.AddTransient<StreamingSettingsViewModel>();
            collection.AddTransient<OutputSettingsViewModel>();
            collection.AddTransient<AccountsSettingsViewModel>();
        }
    }
}
