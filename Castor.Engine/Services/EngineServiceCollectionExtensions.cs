using Microsoft.Extensions.DependencyInjection;

namespace Castor.Engine.Services;

public static class EngineServiceCollectionExtensions
{
    public static IServiceCollection AddCastorEngine(this IServiceCollection services)
    {
        services.AddSingleton<INativeCaptureService, NativeCaptureService>();
        services.AddSingleton<IMediaMtxService, MediaMtxService>();
        services.AddSingleton<ISceneService, SceneService>();
        services.AddSingleton<IRecorderService, RecorderService>();
        services.AddSingleton<IStudioController, StudioController>();
        services.AddSingleton<INetworkCameraDiscoveryService, NetworkCameraDiscoveryService>();
        services.AddSingleton<IApplicationLifecycleService, ApplicationLifecycleService>();

        return services;
    }
}
