using CastorApplication.Models.Auth;
using CastorApplication.Models.Auth.PKCE;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CastorApplication.Services.Auth.Abstractions
{
    public interface IPkceAuthFlow : IAuthFlow
    {
        Task<BrowserLoginResult> BeginAsync(CancellationToken ct = default);

        Task<AuthSession> CompleteAsync(
            BrowserLoginResult login, CancellationToken ct = default);
    }
}
