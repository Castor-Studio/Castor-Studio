using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CastorApplication.Models.Auth.PKCE
{
    public sealed class BrowserLoginResult
    {
        public required string State { get; init; }

        public required string CodeVerifier { get; init; }

        public required Uri AuthorizationUri { get; init; }
    }
}
