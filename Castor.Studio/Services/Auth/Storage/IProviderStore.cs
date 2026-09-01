using System.Collections.Generic;
using CastorApplication.Models.Settings.Providers;

namespace CastorApplication.Services.Auth.Storage
{
    public interface IProviderStore
    {
        IReadOnlyCollection<ProviderSettings> GetAll();

        ProviderSettings? Get(string providerId);

        void Save(ProviderSettings provider);

        void Delete(string providerId);
    }
}