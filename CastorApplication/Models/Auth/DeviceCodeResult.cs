using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CastorApplication.Models.Auth
{
    public sealed class DeviceCodeResult
    {
        public required string DeviceCode { get; init; }

        public required string UserCode { get; init; }

        public required string VerificationUri { get; init; }

        public string? VerificationUriComplete { get; init; }

        public required TimeSpan ExpiresIn { get; init; }

        public required TimeSpan Interval { get; init; }

        public DateTimeOffset CreatedAt { get; init; } =
            DateTimeOffset.UtcNow;

        public bool IsExpired =>
            DateTimeOffset.UtcNow >= CreatedAt + ExpiresIn;
    }
}
