using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CastorApplication.Services.Auth.Common.Localhost
{
    public sealed class LocalAuthResult
    {
        public required string Code { get; init; }

        public required string State { get; init; }
    }
}
