using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Castor.Engine.Models;

namespace Castor.Engine.Services;

public sealed class NetworkCameraDiscoveryService : INetworkCameraDiscoveryService
{
    private const string MulticastAddress = "239.255.255.250";
    private const int WsDiscoveryPort = 3702;

    private static readonly int[] RtspPorts = [554, 8554, 10554];

    public async Task<IReadOnlyList<DiscoveredCamera>> ScanAsync(TimeSpan? timeout = null)
    {
        var duration = timeout ?? TimeSpan.FromSeconds(4);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<DiscoveredCamera>();

        var onvifTask = ScanOnvifAsync(duration);
        var rtspTask = ScanRtspPortsAsync(duration);

        await Task.WhenAll(onvifTask, rtspTask);

        foreach (var camera in onvifTask.Result.Concat(rtspTask.Result))
        {
            if (seen.Add(camera.Ip))
                results.Add(camera);
        }

        return results;
    }

    private static string BuildProbeMessage() =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <e:Envelope xmlns:e="http://www.w3.org/2003/05/soap-envelope"
                    xmlns:w="http://schemas.xmlsoap.org/ws/2004/08/addressing"
                    xmlns:d="http://schemas.xmlsoap.org/ws/2005/04/discovery"
                    xmlns:dn="http://www.onvif.org/ver10/network/wsdl">
          <e:Header>
            <w:MessageID>uuid:{Guid.NewGuid()}</w:MessageID>
            <w:To>urn:schemas-xmlsoap-org:ws:2005:04:discovery</w:To>
            <w:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</w:Action>
          </e:Header>
          <e:Body>
            <d:Probe>
              <d:Types>dn:NetworkVideoTransmitter</d:Types>
            </d:Probe>
          </e:Body>
        </e:Envelope>
        """;

    private static async Task<List<DiscoveredCamera>> ScanOnvifAsync(TimeSpan duration)
    {
        var results = new List<DiscoveredCamera>();
        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

            var probe = Encoding.UTF8.GetBytes(BuildProbeMessage());
            var target = new IPEndPoint(IPAddress.Parse(MulticastAddress), WsDiscoveryPort);
            await udp.SendAsync(probe, probe.Length, target);

            using var cts = new CancellationTokenSource(duration);
            var seen = new HashSet<string>();
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var receive = await udp.ReceiveAsync(cts.Token);
                    var ip = receive.RemoteEndPoint.Address.ToString();
                    if (!seen.Add(ip)) continue;

                    var body = Encoding.UTF8.GetString(receive.Buffer);
                    var url = ExtractRtspUrl(body, ip);
                    var label = ExtractFriendlyName(body) ?? $"Caméra ONVIF ({ip})";

                    results.Add(new DiscoveredCamera
                    {
                        Label = label,
                        Ip = ip,
                        SuggestedUrl = url,
                        Method = "ONVIF",
                    });
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException)
                {
                    break;
                }
            }
        }
        catch
        {
        }

        return results;
    }

    private static async Task<List<DiscoveredCamera>> ScanRtspPortsAsync(TimeSpan budget)
    {
        var results = new List<DiscoveredCamera>();
        var subnets = GetLocalSubnets();
        if (subnets.Count == 0) return results;

        var perIpTimeout = TimeSpan.FromMilliseconds(400);
        var tasks = new List<Task<DiscoveredCamera?>>();

        foreach (var subnet in subnets)
        {
            for (int i = 1; i <= 254; i++)
            {
                var ip = $"{subnet}.{i}";
                tasks.Add(ProbeRtspAsync(ip, perIpTimeout));
            }
        }

        using var cts = new CancellationTokenSource(budget);
        try
        {
            await Task.WhenAll(tasks).WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        foreach (var task in tasks)
        {
            if (task.IsCompletedSuccessfully && task.Result != null)
                results.Add(task.Result);
        }

        return results;
    }

    private static async Task<DiscoveredCamera?> ProbeRtspAsync(string ip, TimeSpan timeout)
    {
        foreach (var port in RtspPorts)
        {
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(ip, port).WaitAsync(timeout);
                if (tcp.Connected)
                {
                    return new DiscoveredCamera
                    {
                        Label = $"Caméra RTSP ({ip}:{port})",
                        Ip = ip,
                        SuggestedUrl = $"rtsp://{ip}:{port}/stream",
                        Method = "RTSP",
                    };
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static List<string> GetLocalSubnets()
    {
        var subnets = new HashSet<string>();
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up) continue;
            if (networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

            foreach (var address in networkInterface.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var parts = address.Address.ToString().Split('.');
                if (parts.Length == 4 && parts[0] != "127")
                    subnets.Add($"{parts[0]}.{parts[1]}.{parts[2]}");
            }
        }

        return [.. subnets];
    }

    private static string ExtractRtspUrl(string xml, string senderIp)
    {
        var match = Regex.Match(xml, @"<[^>]*XAddrs[^>]*>(.*?)</[^>]*XAddrs>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (match.Success)
        {
            foreach (var address in match.Groups[1].Value.Trim().Split(' '))
            {
                if (!address.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) continue;
                var uri = new Uri(address);
                return $"rtsp://{uri.Host}:{(uri.Port > 0 ? uri.Port : 554)}/stream";
            }
        }

        return $"rtsp://{senderIp}:554/stream";
    }

    private static string? ExtractFriendlyName(string xml)
    {
        var match = Regex.Match(
            xml,
            @"<[^>]*(?:FriendlyName|Manufacturer|Model)[^>]*>(.*?)</",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return match.Success ? match.Groups[1].Value.Trim() : null;
    }
}
