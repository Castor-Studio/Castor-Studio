using System;
using System.Collections.Generic;
using System.Linq;
using CastorApplication.Services.Auth.Abstractions;

namespace CastorApplication.Services.Auth.Providers
{
    public sealed class ProviderRegistry
    {
        private readonly Dictionary<string, IAuthProvider> _providers;

        public ProviderRegistry(
            IEnumerable<IAuthProvider> providers)
        {
            _providers = providers.ToDictionary(
                x => x.Id,
                StringComparer.OrdinalIgnoreCase);
        }

        public IAuthProvider Get(string providerId)
        {
            if (!_providers.TryGetValue(
                    providerId,
                    out var provider))
            {
                throw new InvalidOperationException(
                    $"Provider '{providerId}' not found.");
            }

            return provider;
        }

        public IReadOnlyCollection<IAuthProvider> GetAll()
        {
            return _providers.Values;
        }
    }
}
