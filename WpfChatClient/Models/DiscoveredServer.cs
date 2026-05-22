using CommunityToolkit.Mvvm.ComponentModel;

namespace WpfChatClient.Models;

public partial class DiscoveredServer : ObservableObject
{
    [ObservableProperty]
    private string _hostName = string.Empty;

    [ObservableProperty]
    private string _ipAddress = string.Empty;

    [ObservableProperty]
    private int _port;

    [ObservableProperty]
    private int _onlineCount;

    [ObservableProperty]
    private string _serverVersion = "1.0";

    [ObservableProperty]
    private bool _isSelected;

    public string DisplayName => $"{HostName} ({IpAddress})";
    public string DisplayInfo => $"{OnlineCount} online • Port {Port}";
}
