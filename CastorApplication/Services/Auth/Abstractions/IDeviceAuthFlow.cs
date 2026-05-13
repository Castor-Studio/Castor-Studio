using CastorApplication.Models.Auth;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CastorApplication.Services.Auth.Abstractions
{
    public interface IDeviceAuthFlow : IAuthFlow
    {
        Task<DeviceCodeResult> BeginAsync(CancellationToken ct = default);

        Task<AuthSession> PollAsync(
            DeviceCodeResult deviceCode, CancellationToken ct = default);
    }
}
