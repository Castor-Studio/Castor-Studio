using CastorApplication.Services.Auth;
using CastorApplication.Services.Auth.Abstractions;
using CastorApplication.Services.Auth.Common.Localhost;
using CastorApplication.Services.Auth.Common.PKCE;
using CastorApplication.Services.Auth.Providers;
using CastorApplication.Services.Auth.Providers.Twitch;
using CastorApplication.Services.Auth.Providers.Youtube;
using CastorApplication.Services.Auth.Storage;
using CastorApplication.Services.Config;
using CastorApplication.Services.Settings;
using CastorApplication.ViewModels;
using CastorApplication.ViewModels.Settings;
using CastorApplication.ViewModels.Settings.Sections;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http;
using TwitchLib.Api;

namespace CastorApplication
{
    public static class ServiceCollectionExtensions
    {
        public static void AddCommonServices(this IServiceCollection collection)
        {
            collection.AddSingleton<HttpClient>();
            collection.AddSingleton<HttpListener>();
            collection.AddSingleton<TwitchAPI>();
            collection.AddSingleton<YoutubeApiClient>();

            collection.AddSingleton<ITokenStore, InMemoryTokenStore>();
            collection.AddSingleton<IProviderStore, ProviderStore>();

            collection.AddSingleton<IConfigService, JsonConfigService>();

            // Auth
            collection.AddTransient<PkceGenerator>();
            collection.AddTransient<ILocalAuthServer, LocalAuthServer>();

            collection.AddTransient<TwitchSessionFactory>();
            collection.AddTransient<TwitchDeviceAuthFlow>();
            collection.AddTransient<IAuthProvider, TwitchAuthProvider>();

            collection.AddTransient<YoutubeSessionFactory>();
            collection.AddTransient<YoutubeAuthFlow>();
            collection.AddTransient<IAuthProvider, YoutubeAuthProvider>();

            collection.AddSingleton<ProviderRegistry>();
            collection.AddSingleton<IAuthSessionService, AuthSessionService>();
            collection.AddSingleton<IAuthService, AuthService>();

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
