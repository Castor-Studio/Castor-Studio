using System.Net;
using CastorApplication.Models.Studio;
using CastorApplication.Services.Studio;

namespace Castor.Studio.Tests;

public sealed class RtmpDiscoveryServiceTests
{
    [Fact]
    public void Candidates_start_with_this_machine_then_sweep_each_local_slash_24_once()
    {
        var hosts = RtmpDiscoveryService.CandidateHosts(
        [
            IPAddress.Parse("192.168.1.10"),
            IPAddress.Parse("192.168.1.11"),
            IPAddress.Parse("10.0.0.5"),
            IPAddress.Loopback,
            IPAddress.IPv6Loopback,
            IPAddress.Parse("fe80::1")
        ]);

        Assert.Equal(IPAddress.Loopback, hosts[0]);
        Assert.Equal(1 + 252 + 253, hosts.Count);
        Assert.Equal(hosts.Count, hosts.Distinct().Count());
        Assert.DoesNotContain(IPAddress.Parse("192.168.1.10"), hosts);
        Assert.DoesNotContain(IPAddress.Parse("192.168.1.0"), hosts);
        Assert.DoesNotContain(IPAddress.Parse("192.168.1.255"), hosts);
        Assert.Contains(IPAddress.Parse("192.168.1.254"), hosts);
        Assert.Contains(IPAddress.Parse("10.0.0.1"), hosts);
    }

    [Fact]
    public void Without_a_network_only_this_machine_is_probed()
    {
        Assert.Equal([IPAddress.Loopback], RtmpDiscoveryService.CandidateHosts([]));
    }

    [Fact]
    public async Task Scan_returns_the_devices_that_accept_rtmp_in_sweep_order_with_their_names()
    {
        var open = new HashSet<IPAddress> { IPAddress.Parse("192.168.1.30"), IPAddress.Loopback, IPAddress.Parse("192.168.1.2") };
        var service = new RtmpDiscoveryService(
            () => [IPAddress.Parse("192.168.1.10")],
            (host, _) => Task.FromResult(open.Contains(host)),
            (host, _) => Task.FromResult(host.Equals(IPAddress.Parse("192.168.1.30")) ? "camera-plateau" : ""));

        var servers = await service.ScanAsync(CancellationToken.None);

        Assert.Equal(["127.0.0.1", "192.168.1.2", "192.168.1.30"], servers.Select(server => server.Address));
        Assert.Equal("camera-plateau", servers[2].Title);
        Assert.Equal("192.168.1.2", servers[1].Title);
        Assert.Equal("rtmp://192.168.1.30:1935", servers[2].Server);
    }

    [Fact]
    public async Task Scan_stops_when_cancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var service = new RtmpDiscoveryService(
            () => [IPAddress.Parse("192.168.1.10")],
            (_, token) =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.FromResult(false);
            },
            (_, _) => Task.FromResult(""));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ScanAsync(cancellation.Token));
    }
}
