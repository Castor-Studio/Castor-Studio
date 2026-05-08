using Microsoft.Extensions.DependencyInjection;
using CastorApplication.ViewModels;

namespace CastorApplication
{
    public static class ServiceCollectionExtensions
    {
        public static void AddCommonServices(this IServiceCollection collection)
        {
            collection.AddTransient<MainViewModel>();
        }
    }
}
