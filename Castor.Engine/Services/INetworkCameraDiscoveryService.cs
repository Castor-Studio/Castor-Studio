using Castor.Engine.Models;

namespace Castor.Engine.Services;

public interface INetworkCameraDiscoveryService
{
    Task<IReadOnlyList<DiscoveredCamera>> ScanAsync(TimeSpan? timeout = null);
}
