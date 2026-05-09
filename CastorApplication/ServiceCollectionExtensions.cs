using CastorApplication.Models.Auth.Options;
using CastorApplication.Services.Auth;
using CastorApplication.Services.Auth.Providers;
using CastorApplication.Services.Auth.Providers.Twitch;
using CastorApplication.Services.Auth.Storage;
using CastorApplication.Services.Config;
using CastorApplication.Services.Settings;
using CastorApplication.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;

namespace CastorApplication
{
    public static class ServiceCollectionExtensions
    {
        public static void AddCommonServices(this IServiceCollection collection)
        {
            collection.AddSingleton<HttpClient>();

            collection.AddSingleton<IConfigService, JsonConfigService>();

            collection.AddSingleton<IAuthProvider, TwitchAuthProvider>();

            collection.AddSingleton<IAuthService, AuthService>();
            collection.AddSingleton<ITokenStore, InMemoryTokenStore>();
            collection.AddSingleton<ProviderRegistry>();

            collection.AddSingleton<SettingsService>();

            collection.AddTransient<SettingsViewModel>();
            collection.AddTransient<MainViewModel>();
        }
    }
}
