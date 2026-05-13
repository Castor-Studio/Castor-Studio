using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CastorApplication.Services.Auth.Common.Localhost;

namespace CastorApplication.Services.Auth.Abstractions
{
    public interface ILocalAuthServer
    {
        Task<LocalAuthResult> WaitForCallbackAsync(
            CancellationToken ct = default);
    }
}
