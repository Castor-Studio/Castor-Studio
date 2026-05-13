using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CastorApplication.Services.Auth.Common.PKCE
{
    public sealed class PkceChallenge
    {
        public required string Verifier { get; init; }

        public required string Challenge { get; init; }
    }
}
