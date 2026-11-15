using CastorApplication.Models.Config;

namespace CastorApplication.Services.Config
{
    public interface IConfigService
    {
        AppConfig Config { get; }
        ProviderConfig GetProviderConfig(string name);
    }
}
