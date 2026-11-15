using CastorApplication.Models.Settings.Providers;

namespace CastorApplication.Services.Auth.Storage
{
    public interface IProviderStore
    {
        event EventHandler? Changed;

        IReadOnlyCollection<ProviderSettings> GetAll();

        ProviderSettings? Get(string providerId);

        void Save(ProviderSettings provider);

        void Delete(string providerId);
    }
}
