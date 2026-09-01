using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CastorApplication.Models.Auth.Options
{
    public sealed class TwitchAuthOptions
    {
        public required string ClientId { get; init; }

        public string[] Scopes { get; init; } = [];
    }
}
