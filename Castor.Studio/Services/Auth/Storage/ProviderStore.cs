using CastorApplication.Models.Settings.Providers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CastorApplication.Services.Auth.Storage
{
    public sealed class ProviderStore : JsonFileStore<ProvidersSettings>, IProviderStore
    {
        private readonly ProvidersSettings _settings;

        private const string _currentAppFolderName = "castor-studio";

        public ProviderStore() : base(BuildDefaultSettingsPath(_currentAppFolderName))
        {
            _settings = Load();
        }

        private static string BuildDefaultSettingsPath(string appFolderName)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, appFolderName, "providers.json");
        }

        public IReadOnlyCollection<ProviderSettings> GetAll()
        {
            return _settings.Providers;
        }

        public ProviderSettings? Get(string providerId)
        {
            return _settings.Providers.FirstOrDefault(p => p.ProviderId == providerId);
        }

        public void Delete(string providerId)
        {
            _settings.Providers.RemoveAll(p => p.ProviderId == providerId);
            Save(_settings);
        }

        public void Save(ProviderSettings provider)
        {
            var existingProviderIndex =
                _settings.Providers.FindIndex(p => p.ProviderId == provider.ProviderId);

            if (existingProviderIndex >= 0)
            {
                _settings.Providers[existingProviderIndex] = provider;
            }
            else
            {
                _settings.Providers.Add(provider);
            }

            Save(_settings);
        }
    }
}
