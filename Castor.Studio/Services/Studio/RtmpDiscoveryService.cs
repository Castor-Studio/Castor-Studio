using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using CastorApplication.Models.Studio;

namespace CastorApplication.Services.Studio;

internal interface IRtmpDiscoveryService
{
    Task<IReadOnlyList<DiscoveredRtmpServer>> ScanAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Finds the devices of the local network that accept RTMP connections on the standard port.
/// </summary>
/// <remarks>
/// RTMP has no discovery and no way to list the streams of a device: an open port is all
/// that can be found. Each device is named by its network name when it has one.
/// </remarks>
internal sealed class RtmpDiscoveryService : IRtmpDiscoveryService
{
    private const int MaxConcurrentProbes = 64;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan HostNameTimeout = TimeSpan.FromSeconds(1);

    private readonly Func<IReadOnlyList<IPAddress>> _localAddresses;
    private readonly Func<IPAddress, CancellationToken, Task<bool>> _isRtmpOpen;
    private readonly Func<IPAddress, CancellationToken, Task<string>> _hostName;

    public RtmpDiscoveryService()
        : this(GetLocalAddresses, IsRtmpOpenAsync, GetHostNameAsync)
    {
    }

    internal RtmpDiscoveryService(
        Func<IReadOnlyList<IPAddress>> localAddresses,
        Func<IPAddress, CancellationToken, Task<bool>> isRtmpOpen,
        Func<IPAddress, CancellationToken, Task<string>> hostName)
    {
        _localAddresses = localAddresses;
        _isRtmpOpen = isRtmpOpen;
        _hostName = hostName;
    }

    public async Task<IReadOnlyList<DiscoveredRtmpServer>> ScanAsync(CancellationToken cancellationToken)
    {
        var hosts = CandidateHosts(_localAddresses());
        using var gate = new SemaphoreSlim(MaxConcurrentProbes);
        var found = await Task.WhenAll(hosts.Select(async host =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                if (!await _isRtmpOpen(host, cancellationToken)) return null;
            }
            finally
            {
                gate.Release();
            }

            return new DiscoveredRtmpServer(host.ToString(), await _hostName(host, cancellationToken));
        }));

        // Same order as the sweep: this machine first, then the network host by host.
        return found.OfType<DiscoveredRtmpServer>().ToList();
    }

    /// <summary>
    /// This machine first, then every other host of the /24 around each local IPv4 address.
    /// A wider subnet is not swept: 254 hosts already take a few seconds.
    /// </summary>
    internal static IReadOnlyList<IPAddress> CandidateHosts(IEnumerable<IPAddress> localAddresses)
    {
        var own = localAddresses
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
            .Distinct()
            .ToList();
        var hosts = new List<IPAddress> { IPAddress.Loopback };
        var seen = new HashSet<IPAddress>(own) { IPAddress.Loopback };
        foreach (var address in own)
        {
            var bytes = address.GetAddressBytes();
            for (var last = 1; last <= 254; last++)
            {
                var host = new IPAddress([bytes[0], bytes[1], bytes[2], (byte)last]);
                if (seen.Add(host)) hosts.Add(host);
            }
        }

        return hosts;
    }

    private static IReadOnlyList<IPAddress> GetLocalAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up &&
                              network.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Select(unicast => unicast.Address)
            .ToList();

    private static async Task<bool> IsRtmpOpenAsync(IPAddress host, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);
        using var socket = new Socket(host.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(host, RtmpStreamUrl.DefaultPort, timeout.Token);
            return true;
        }
        catch (Exception exception) when (exception is SocketException ||
                                          exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    // A device without a reverse DNS entry is shown by its address alone.
    private static async Task<string> GetHostNameAsync(IPAddress host, CancellationToken cancellationToken)
    {
        if (IPAddress.IsLoopback(host)) return "Cet ordinateur";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(HostNameTimeout);
        try
        {
            var entry = await Dns.GetHostEntryAsync(host.ToString(), timeout.Token);
            return entry.HostName == host.ToString() ? "" : entry.HostName;
        }
        catch (Exception exception) when (exception is SocketException ||
                                          exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return "";
        }
    }
}
