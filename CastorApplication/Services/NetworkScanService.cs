using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CastorApplication.Services;

/// <summary>Caméra réseau découverte sur le réseau local.</summary>
public sealed class DiscoveredCamera
{
    public string Label       { get; init; } = "";
    public string Ip          { get; init; } = "";
    public string SuggestedUrl { get; init; } = "";
    public string Method      { get; init; } = ""; // "ONVIF" ou "RTSP"
}

/// <summary>
/// Découverte de caméras réseau :
///  1. WS-Discovery (ONVIF) — probe UDP multicast 239.255.255.250:3702
///  2. Scan TCP port 554 (RTSP) sur tout le sous-réseau local en parallèle
/// Les deux méthodes tournent en même temps ; les résultats sont fusionnés.
/// </summary>
public static class NetworkScanService
{
    private const string MulticastAddress = "239.255.255.250";
    private const int    WsDiscoveryPort  = 3702;

    // Ports RTSP courants à tester
    private static readonly int[] RtspPorts = [554, 8554, 10554];

    // ── API publique ──────────────────────────────────────────────────────────

    /// <summary>
    /// Lance WS-Discovery + scan TCP en parallèle.
    /// Retourne les caméras trouvées (dédupliquées par IP).
    /// </summary>
    public static async Task<List<DiscoveredCamera>> ScanAsync(TimeSpan? timeout = null)
    {
        var duration = timeout ?? TimeSpan.FromSeconds(4);
        var seen     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results  = new List<DiscoveredCamera>();

        // Lance les deux scans en parallèle
        var onvifTask = ScanOnvifAsync(duration);
        var rtspTask  = ScanRtspPortsAsync(duration);

        await Task.WhenAll(onvifTask, rtspTask);

        // Fusionne en donnant priorité à ONVIF (plus d'infos)
        foreach (var cam in onvifTask.Result.Concat(rtspTask.Result))
        {
            if (seen.Add(cam.Ip))
                results.Add(cam);
        }

        return results;
    }

    // ── WS-Discovery (ONVIF) ─────────────────────────────────────────────────

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
            udp.Client.SetSocketOption(SocketOptionLevel.Socket,
                                       SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

            var probe  = Encoding.UTF8.GetBytes(BuildProbeMessage());
            var target = new IPEndPoint(IPAddress.Parse(MulticastAddress), WsDiscoveryPort);
            await udp.SendAsync(probe, probe.Length, target);

            using var cts = new CancellationTokenSource(duration);
            var seen = new HashSet<string>();
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var recv = await udp.ReceiveAsync(cts.Token);
                    var ip   = recv.RemoteEndPoint.Address.ToString();
                    if (!seen.Add(ip)) continue;

                    var body  = Encoding.UTF8.GetString(recv.Buffer);
                    var url   = ExtractRtspUrl(body, ip);
                    var label = ExtractFriendlyName(body) ?? $"Caméra ONVIF ({ip})";

                    results.Add(new DiscoveredCamera
                    {
                        Label        = label,
                        Ip           = ip,
                        SuggestedUrl = url,
                        Method       = "ONVIF",
                    });
                }
                catch (OperationCanceledException) { break; }
                catch (SocketException)            { break; }
            }
        }
        catch { /* réseau non disponible */ }
        return results;
    }

    // ── Scan TCP port 554 ─────────────────────────────────────────────────────

    private static async Task<List<DiscoveredCamera>> ScanRtspPortsAsync(TimeSpan budget)
    {
        var results = new List<DiscoveredCamera>();

        // Récupère les sous-réseaux /24 des interfaces actives
        var subnets = GetLocalSubnets();
        if (subnets.Count == 0) return results;

        // Budget par IP : on limite pour tenir dans la durée globale
        var perIpTimeout = TimeSpan.FromMilliseconds(400);

        var tasks = new List<Task<DiscoveredCamera?>>();
        foreach (var subnet in subnets)
        {
            for (int i = 1; i <= 254; i++)
            {
                var ip = $"{subnet}.{i}";
                tasks.Add(ProbRtspAsync(ip, perIpTimeout));
            }
        }

        // Attend toutes les tâches dans le budget global
        using var cts = new CancellationTokenSource(budget);
        try { await Task.WhenAll(tasks).WaitAsync(cts.Token); }
        catch (OperationCanceledException) { /* timeout global */ }

        foreach (var t in tasks)
        {
            if (t.IsCompletedSuccessfully && t.Result != null)
                results.Add(t.Result);
        }

        return results;
    }

    /// <summary>Tente une connexion TCP sur les ports RTSP courants.</summary>
    private static async Task<DiscoveredCamera?> ProbRtspAsync(string ip, TimeSpan timeout)
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
                        Label        = $"Caméra RTSP ({ip}:{port})",
                        Ip           = ip,
                        SuggestedUrl = $"rtsp://{ip}:{port}/stream",
                        Method       = "RTSP",
                    };
                }
            }
            catch { /* port fermé ou timeout */ }
        }
        return null;
    }

    /// <summary>Retourne les préfixes /24 des interfaces réseau actives (ex: "192.168.1").</summary>
    private static List<string> GetLocalSubnets()
    {
        var subnets = new HashSet<string>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback
                                        or NetworkInterfaceType.Tunnel) continue;

            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var parts = ua.Address.ToString().Split('.');
                if (parts.Length == 4 && parts[0] != "127")
                    subnets.Add($"{parts[0]}.{parts[1]}.{parts[2]}");
            }
        }
        return [.. subnets];
    }

    // ── Parsing ONVIF ─────────────────────────────────────────────────────────

    private static string ExtractRtspUrl(string xml, string senderIp)
    {
        var match = Regex.Match(xml, @"<[^>]*XAddrs[^>]*>(.*?)</[^>]*XAddrs>",
                                RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (match.Success)
        {
            foreach (var addr in match.Groups[1].Value.Trim().Split(' '))
            {
                if (addr.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    var uri = new Uri(addr);
                    return $"rtsp://{uri.Host}:{(uri.Port > 0 ? uri.Port : 554)}/stream";
                }
            }
        }
        return $"rtsp://{senderIp}:554/stream";
    }

    private static string? ExtractFriendlyName(string xml)
    {
        var match = Regex.Match(xml,
            @"<[^>]*(?:FriendlyName|Manufacturer|Model)[^>]*>(.*?)</",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }
}
