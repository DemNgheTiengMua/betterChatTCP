using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Win32;
using WpfChatClient.Core.Interfaces;
using WpfChatClient.Messages;
using WpfChatClient.Models;
using WpfChatClient.Services;

namespace WpfChatClient.ViewModels;

public partial class ConnectViewModel : ObservableObject
{
    private readonly IChatService _chatService;
    private readonly ServerDiscoveryService _discoveryService;
    private readonly ServerHostService _hostService;
    private CancellationTokenSource? _scanCts;

    // ---- Phase management ----
    // "Scanning", "ServerList", "NoServers", "ManualConnect"
    [ObservableProperty]
    private string _currentPhase = "Scanning";

    [ObservableProperty]
    private string _ip = "127.0.0.1";

    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private string _scanStatus = "Scanning for servers on your network...";

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private DiscoveredServer? _selectedServer;

    // ---- Avatar ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvatar))]
    private string? _avatarPath;

    public bool HasAvatar => !string.IsNullOrWhiteSpace(AvatarPath) && File.Exists(AvatarPath);

    public ObservableCollection<DiscoveredServer> DiscoveredServers { get; } = new();

    public ConnectViewModel(IChatService chatService, ServerDiscoveryService discoveryService, ServerHostService hostService)
    {
        _chatService = chatService;
        _discoveryService = discoveryService;
        _hostService = hostService;

        // Auto-scan on construction
        _ = ScanForServersAsync();
    }

    [RelayCommand]
    private async Task ScanForServersAsync()
    {
        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();

        IsScanning = true;
        CurrentPhase = "Scanning";
        ScanStatus = "Scanning for servers on your network...";
        ErrorMessage = "";
        DiscoveredServers.Clear();

        try
        {
            var servers = await _discoveryService.ScanForServersAsync(
                TimeSpan.FromSeconds(3),
                _scanCts.Token);

            if (_scanCts.IsCancellationRequested) return;

            if (servers.Count > 0)
            {
                foreach (var server in servers)
                {
                    DiscoveredServers.Add(server);
                }

                SelectedServer = DiscoveredServers.First();
                CurrentPhase = "ServerList";
            }
            else
            {
                CurrentPhase = "NoServers";
            }
        }
        catch (OperationCanceledException)
        {
            // Scan was cancelled
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SCAN] Error: {ex.Message}");
            CurrentPhase = "NoServers";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private void ShowManualConnect()
    {
        CurrentPhase = "ManualConnect";
        ErrorMessage = "";
    }

    [RelayCommand]
    private void BackToScan()
    {
        ErrorMessage = "";
        _ = ScanForServersAsync();
    }

    [RelayCommand]
    private async Task HostServer()
    {
        ErrorMessage = "";

        if (_hostService.TryStartServer(out var error))
        {
            ScanStatus = "Starting server... waiting for it to come online.";
            IsScanning = true;
            CurrentPhase = "Scanning";

            // Wait a moment for the server to start, then scan
            await Task.Delay(2000);
            await ScanForServersAsync();
        }
        else
        {
            ErrorMessage = error;
        }
    }

    [RelayCommand]
    private async Task ConnectToSelected()
    {
        if (SelectedServer == null)
        {
            ErrorMessage = "Please select a server.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Username))
        {
            ErrorMessage = "Username is required.";
            return;
        }

        await DoConnect(SelectedServer.IpAddress, SelectedServer.Port);
    }

    [RelayCommand]
    private async Task Connect()
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            ErrorMessage = "Username is required.";
            return;
        }

        var serverIp = string.IsNullOrWhiteSpace(Ip) ? "127.0.0.1" : Ip.Trim();

        await DoConnect(serverIp, 5000);
    }

    private async Task DoConnect(string ip, int port)
    {
        try
        {
            ErrorMessage = "";
            IsConnecting = true;

            // Save avatar locally before connecting
            SaveAvatarLocally();

            await _chatService.ConnectAsync(ip, port, Username.Trim());

            // Signal successful connection (with Username and AvatarPath)
            WeakReferenceMessenger.Default.Send(new ConnectionSuccessMessage(Username.Trim(), AvatarPath));
        }
        catch (System.Net.Sockets.SocketException)
        {
            ErrorMessage = "Cannot connect. Check server IP, Wi-Fi, firewall, and that ChatServer is running.";
        }
        catch (TimeoutException)
        {
            ErrorMessage = "Connection timed out. Check the host IP and Wi-Fi network.";
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Connection failed: {ex.Message}";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    [RelayCommand]
    private void PickAvatar()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Avatar Image",
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All Files|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            AvatarPath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void RemoveAvatar()
    {
        AvatarPath = null;
    }

    private void SaveAvatarLocally()
    {
        if (string.IsNullOrWhiteSpace(AvatarPath) || !File.Exists(AvatarPath))
            return;

        try
        {
            var avatarDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ChatTCP", "Avatars");
            Directory.CreateDirectory(avatarDir);

            var ext = Path.GetExtension(AvatarPath);
            var destPath = Path.Combine(avatarDir, $"{Username.Trim()}{ext}");
            File.Copy(AvatarPath, destPath, overwrite: true);
            AvatarPath = destPath;
            Console.WriteLine($"[AVATAR] Saved avatar to {destPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AVATAR] Save failed: {ex.Message}");
        }
    }
}
