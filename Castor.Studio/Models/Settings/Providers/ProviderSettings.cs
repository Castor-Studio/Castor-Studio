using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CastorApplication.Models.Settings.Providers
{
    public sealed class ProviderSettings
    {
        public required string ProviderId { get; set; }

        public bool IsConnected { get; set; }

        public string? StreamKey { get; set; }

        // Server the live output pushes to. Empty for Twitch, which picks its own ("auto").
        public string? IngestUrl { get; set; }

        // Platform-side stream the key belongs to (YouTube liveStream id), reused across sessions.
        public string? StreamId { get; set; }

        public string? UserId { get; set; }

        public string? UserName { get; set; }
    }
}
