using CastorApplication.Models.Config;
using Newtonsoft.Json;
using System.Collections.Generic;
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
            : this(path, CredentialsPathBeside(path), Environment.GetEnvironmentVariable)
        {
        }

        // OAuth clients, by provider: their own file, so replacing one never touches the rest
        // of the configuration.
        internal const string CredentialsFileName = "credentials.json";

        internal JsonConfigService(string path, string credentialsPath, Func<string, string?> environment)
        {
            var json = File.ReadAllText(path);
            Config = JsonConvert.DeserializeObject<AppConfig>(json)
                     ?? throw new Exception("Invalid config file");
            // From the least to the most specific: config.json, credentials.json, environment.
            ApplyCredentialsFile(Config, credentialsPath);
            ApplyEnvironment(Config, environment);
        }

        private static string CredentialsPathBeside(string configPath) =>
            Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? "", CredentialsFileName);

        // Optional: a build without it keeps the clients of config.json.
        private static void ApplyCredentialsFile(AppConfig config, string credentialsPath)
        {
            if (!File.Exists(credentialsPath)) return;

            var credentials = JsonConvert.DeserializeObject<Dictionary<string, ProviderCredentials>>(
                                  File.ReadAllText(credentialsPath))
                              ?? throw new Exception("Invalid credentials file");
            foreach (var (providerId, provider) in config.Providers)
            {
                if (credentials.TryGetValue(providerId, out var client))
                    ApplyClient(provider, client.ClientId, client.ClientSecret);
            }
        }

        // CASTOR_<PROVIDER>_CLIENT_ID and CASTOR_<PROVIDER>_CLIENT_SECRET, when set, win over both
        // files: a developer can try another OAuth client without editing them.
        private static void ApplyEnvironment(AppConfig config, Func<string, string?> environment)
        {
            foreach (var (providerId, provider) in config.Providers)
            {
                var prefix = $"CASTOR_{providerId.ToUpperInvariant()}_";
                ApplyClient(provider, environment(prefix + "CLIENT_ID"), environment(prefix + "CLIENT_SECRET"));
            }
        }

        // A blank value never erases what a less specific source gave.
        private static void ApplyClient(ProviderConfig provider, string? clientId, string? clientSecret)
        {
            if (!string.IsNullOrWhiteSpace(clientId))
                provider.ClientId = clientId.Trim();
            if (!string.IsNullOrWhiteSpace(clientSecret))
                provider.ClientSecret = clientSecret.Trim();
        }

        public ProviderConfig GetProviderConfig(string providerId)
        {
            if (!Config.Providers.TryGetValue(providerId, out var provider))
                throw new Exception($"Provider '{providerId}' not found");

            return provider;
        }
    }
}
