namespace WpfChatClient.Core.Models;

/// <summary>
/// Represents a chat server discovered via UDP broadcast.
/// </summary>
public class ServerInfo
{
    public string DisplayName { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public int Port { get; set; } = 5000;
    public int OnlineCount { get; set; }

    public string Label => $"{DisplayName}  ({OnlineCount} online)  — {Ip}:{Port}";
}

/// <summary>
/// JSON payload returned by the server in response to a discovery probe.
/// </summary>
public class ServerDiscoveryResponse
{
    public string? ServerName { get; set; }
    public string Ip { get; set; } = string.Empty;
    public int Port { get; set; } = 5000;
    public int OnlineCount { get; set; }
}
