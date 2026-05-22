using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WpfChatClient.Models;

namespace WpfChatClient.Services;

public class DiscoveryResponse
{
    public string HostName { get; set; } = string.Empty;
    public int Port { get; set; }
    public int OnlineCount { get; set; }
    public string ServerVersion { get; set; } = "1.0";
}

public class ServerDiscoveryService
{
    private const int DiscoveryPort = 5001;
    private const string DiscoveryProbe = "CHATTCP_DISCOVER";

    /// <summary>
    /// Scans the LAN for chat servers by sending a UDP broadcast probe.
    /// Returns a list of discovered servers within the timeout period.
    /// </summary>
    public async Task<List<DiscoveredServer>> ScanForServersAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var servers = new List<DiscoveredServer>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clientsToDispose = new List<UdpClient>();

        try
        {
            var localIps = GetLocalIPv4Addresses();
            // Fallback to loopback if no external IPs found
            if (localIps.Count == 0)
            {
                localIps.Add("127.0.0.1");
            }

            var probeBytes = Encoding.UTF8.GetBytes(DiscoveryProbe);
            var receiveTasks = new List<Task<(UdpReceiveResult Result, UdpClient Client)>>();

            foreach (var localIpStr in localIps)
            {
                try
                {
                    if (!IPAddress.TryParse(localIpStr, out var localIp)) continue;

                    // Bind to the specific local interface IP on an ephemeral port
                    var client = new UdpClient(new IPEndPoint(localIp, 0));
                    client.EnableBroadcast = true;
                    client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    clientsToDispose.Add(client);

                    // Send general broadcast 255.255.255.255 from this interface
                    await client.SendAsync(probeBytes, probeBytes.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));

                    // Send directed broadcast for this interface subnet
                    var parts = localIpStr.Split('.');
                    if (parts.Length == 4)
                    {
                        var directedBroadcast = $"{parts[0]}.{parts[1]}.{parts[2]}.255";
                        if (IPAddress.TryParse(directedBroadcast, out var directedEp))
                        {
                            await client.SendAsync(probeBytes, probeBytes.Length, new IPEndPoint(directedEp, DiscoveryPort));
                        }
                    }

                    // Also send to loopback if this is the loopback interface
                    if (IPAddress.IsLoopback(localIp))
                    {
                        await client.SendAsync(probeBytes, probeBytes.Length, new IPEndPoint(IPAddress.Loopback, DiscoveryPort));
                    }

                    // Start background receive task for this client
                    receiveTasks.Add(ReceiveWithClientAsync(client, cancellationToken));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DISCOVERY] Interface binding failed for {localIpStr}: {ex.Message}");
                }
            }

            // Fallback general 0.0.0.0 client to be absolutely sure
            try
            {
                var generalClient = new UdpClient();
                generalClient.EnableBroadcast = true;
                generalClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                clientsToDispose.Add(generalClient);

                await generalClient.SendAsync(probeBytes, probeBytes.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
                await generalClient.SendAsync(probeBytes, probeBytes.Length, new IPEndPoint(IPAddress.Loopback, DiscoveryPort));

                receiveTasks.Add(ReceiveWithClientAsync(generalClient, cancellationToken));
            }
            catch { }

            // Collect responses until timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            while (receiveTasks.Count > 0 && !timeoutCts.Token.IsCancellationRequested)
            {
                var completedTask = await Task.WhenAny(receiveTasks);
                receiveTasks.Remove(completedTask);

                try
                {
                    var (result, client) = await completedTask;

                    // Parse response
                    var responseJson = Encoding.UTF8.GetString(result.Buffer);
                    var response = JsonSerializer.Deserialize<DiscoveryResponse>(responseJson);

                    if (response != null)
                    {
                        var key = $"{result.RemoteEndPoint.Address}:{response.Port}";
                        if (seen.Add(key))
                        {
                            servers.Add(new DiscoveredServer
                            {
                                HostName = response.HostName,
                                IpAddress = result.RemoteEndPoint.Address.ToString(),
                                Port = response.Port,
                                OnlineCount = response.OnlineCount,
                                ServerVersion = response.ServerVersion
                            });
                        }
                    }

                    // Keep listening on this client
                    if (!timeoutCts.Token.IsCancellationRequested)
                    {
                        receiveTasks.Add(ReceiveWithClientAsync(client, timeoutCts.Token));
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // If task failed, don't re-add it
                    Console.WriteLine($"[DISCOVERY] Receive error: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DISCOVERY] Scan failed: {ex.Message}");
        }
        finally
        {
            foreach (var client in clientsToDispose)
            {
                try { client.Dispose(); } catch { }
            }
        }

        return servers;
    }

    private async Task<(UdpReceiveResult Result, UdpClient Client)> ReceiveWithClientAsync(UdpClient client, CancellationToken cancellationToken)
    {
        var result = await client.ReceiveAsync(cancellationToken);
        return (result, client);
    }

    private List<string> GetLocalIPv4Addresses()
    {
        var ips = new List<string>();
        try
        {
            foreach (var networkInterface in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up ||
                    networkInterface.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback ||
                    networkInterface.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Tunnel)
                {
                    continue;
                }
                var properties = networkInterface.GetIPProperties();
                foreach (var unicastAddress in properties.UnicastAddresses)
                {
                    if (unicastAddress.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(unicastAddress.Address))
                    {
                        ips.Add(unicastAddress.Address.ToString());
                    }
                }
            }
        }
        catch { }
        return ips;
    }
}
