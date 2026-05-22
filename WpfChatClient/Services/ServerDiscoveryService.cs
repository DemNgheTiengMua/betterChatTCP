using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WpfChatClient.Core.Models;

namespace WpfChatClient.Services;

/// <summary>
/// Discovers chat servers on the local network using UDP broadcast probes.
/// The server listens on <see cref="DiscoveryPort"/> and replies with a <see cref="ServerDiscoveryResponse"/>.
/// </summary>
public class ServerDiscoveryService
{
    public const int DiscoveryPort = 5002;
    private const string ProbeMessage = "CHAT_DISCOVER_V1";
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Broadcasts a UDP probe and returns all responding servers within the scan timeout.
    /// </summary>
    public static async Task<List<ServerInfo>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<ServerInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var udp = new UdpClient();
        udp.EnableBroadcast = true;
        udp.Client.ReceiveTimeout = (int)ScanTimeout.TotalMilliseconds;

        var probeBytes = Encoding.UTF8.GetBytes(ProbeMessage);
        var broadcastEp = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);

        try
        {
            await udp.SendAsync(probeBytes, probeBytes.Length, broadcastEp).ConfigureAwait(false);
            Console.WriteLine($"[DISCOVERY] Probe sent to {broadcastEp}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DISCOVERY] Failed to send probe: {ex.Message}");
            return results;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ScanTimeout);

        while (!timeoutCts.Token.IsCancellationRequested)
        {
            try
            {
                var receiveTask = udp.ReceiveAsync(timeoutCts.Token);
                var result = await receiveTask.ConfigureAwait(false);
                var json = Encoding.UTF8.GetString(result.Buffer);
                var response = JsonSerializer.Deserialize<ServerDiscoveryResponse>(json);

                if (response != null && !string.IsNullOrWhiteSpace(response.Ip))
                {
                    var key = $"{response.Ip}:{response.Port}";
                    if (seen.Add(key))
                    {
                        results.Add(new ServerInfo
                        {
                            DisplayName = response.ServerName ?? response.Ip,
                            Ip = response.Ip,
                            Port = response.Port,
                            OnlineCount = response.OnlineCount
                        });
                        Console.WriteLine($"[DISCOVERY] Found server: {key} ({response.ServerName})");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                break; // receive timeout or socket error
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DISCOVERY] Error receiving response: {ex.Message}");
            }
        }

        return results;
    }
}
