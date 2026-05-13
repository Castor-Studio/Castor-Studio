using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CastorApplication.Models.Auth;

namespace CastorApplication.Services.Auth
{
    public interface IAuthService
    {   
        Task<AuthSession> LoginAsync(
            string providerId,
            CancellationToken ct = default);
    }
}
