using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CastorApplication.Models.Auth
{
    public sealed class AuthSession
    {   
        public string ProviderId { get; init; } = "";

        public string AccessToken { get; init; } = "";

        public string? RefreshToken { get; init; }

        public DateTimeOffset ExpiresAt { get; init; }

        public string[] Scopes { get; init; } = [];

        public UserProfile? Profile { get; set; }

        public bool IsExpired(TimeSpan? tolerance = null)
        {
            tolerance ??= TimeSpan.FromMinutes(1);

            return DateTimeOffset.UtcNow >=
                   ExpiresAt - tolerance.Value;
        }
    }
}
