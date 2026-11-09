using CastorApplication.Models.Config;
using Newtonsoft.Json;
using System.IO;
using System;

namespace CastorApplication.Services.Config
{
    public class JsonConfigService : IConfigService
    {
        public AppConfig Config { get; }

        public JsonConfigService()
            : this(Path.Combine(AppContext.BaseDirectory, "config.json"))
        {
        }

        public JsonConfigService(string path)
            : this(path, Environment.GetEnvironmentVariable)
        {
        }

        internal JsonConfigService(string path, Func<string, string?> environment)
        {
            var json = File.ReadAllText(path);
            Config = JsonConvert.DeserializeObject<AppConfig>(json)
                     ?? throw new Exception("Invalid config file");
            ApplyEnvironment(Config, environment);
        }

        // CASTOR_<PROVIDER>_CLIENT_ID and CASTOR_<PROVIDER>_CLIENT_SECRET, when set, replace the
        // client of config.json: a developer can try another OAuth client without editing it.
        private static void ApplyEnvironment(AppConfig config, Func<string, string?> environment)
        {
            foreach (var (providerId, provider) in config.Providers)
            {
                var prefix = $"CASTOR_{providerId.ToUpperInvariant()}_";

                var clientId = environment(prefix + "CLIENT_ID");
                if (!string.IsNullOrWhiteSpace(clientId))
                    provider.ClientId = clientId.Trim();

                var clientSecret = environment(prefix + "CLIENT_SECRET");
                if (!string.IsNullOrWhiteSpace(clientSecret))
                    provider.ClientSecret = clientSecret.Trim();
            }
        }

        public ProviderConfig GetProviderConfig(string providerId)
        {
            if (!Config.Providers.TryGetValue(providerId, out var provider))
                throw new Exception($"Provider '{providerId}' not found");

            return provider;
        }
    }
}
