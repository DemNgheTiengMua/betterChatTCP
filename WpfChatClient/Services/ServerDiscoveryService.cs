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
        UdpClient? udpClient = null;

        try
         {
             udpClient = new UdpClient();
             udpClient.EnableBroadcast = true;
             udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
 
             // Send discovery probe as broadcast
             var probeBytes = Encoding.UTF8.GetBytes(DiscoveryProbe);
             await udpClient.SendAsync(probeBytes, probeBytes.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
 
             // Also try sending to localhost for same-machine servers
             try
             {
                 await udpClient.SendAsync(probeBytes, probeBytes.Length, new IPEndPoint(IPAddress.Loopback, DiscoveryPort));
             }
             catch
             {
                 // Ignore - broadcast should already cover localhost on most systems
             }
 
             // Collect responses until timeout
             using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
             timeoutCts.CancelAfter(timeout);
 
             while (!timeoutCts.Token.IsCancellationRequested)
             {
                 try
                 {
                     var receiveTask = udpClient.ReceiveAsync(timeoutCts.Token);
                     var result = await receiveTask;
 
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
                 }
                 catch (OperationCanceledException)
                 {
                     break;
                 }
                 catch (SocketException)
                 {
                     // Timeout or network error — stop listening
                     break;
                 }
                 catch (Exception ex)
                 {
                     Console.WriteLine($"[DISCOVERY] Parse error: {ex.Message}");
                 }
             }
         }
         catch (Exception ex)
         {
             Console.WriteLine($"[DISCOVERY] Scan failed: {ex.Message}");
         }
         finally
         {
             udpClient?.Dispose();
         }
 
         return servers;
     }
}
