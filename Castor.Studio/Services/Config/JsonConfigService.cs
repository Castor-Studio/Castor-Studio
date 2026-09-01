using CastorApplication.Models.Config;
using Newtonsoft.Json;
using System.IO;
using System;

namespace CastorApplication.Services.Config
{
    public class JsonConfigService : IConfigService
    {
        public AppConfig Config { get; }

        public JsonConfigService(string path = "config.json")
        {
            var json = File.ReadAllText(path);
            Config = JsonConvert.DeserializeObject<AppConfig>(json)
                     ?? throw new Exception("Invalid config file");
        }

        public ProviderConfig GetProviderConfig(string providerId)
        {
            if (!Config.Providers.TryGetValue(providerId, out var provider))
                throw new Exception($"Provider '{providerId}' not found");

            return provider;
        }
    }
}
