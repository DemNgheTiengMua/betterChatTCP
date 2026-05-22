using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using WpfChatClient.Core.Interfaces;
using WpfChatClient.Core.Models;
using WpfChatClient.Messages;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace WpfChatClient.ViewModels;

public enum ConnectPhase
{
    Scanning,       // Auto-scanning in progress
    ServerList,     // Showing list of found servers (may be empty)
    ManualEntry,    // User wants to type IP manually
    Connecting,     // Attempting TCP connection
}

public partial class ConnectViewModel : ObservableObject
{
    private readonly IChatService _chatService;
    private CancellationTokenSource? _scanCts;

    // ──────────────────────────────────────────────
    // Phase / State
    // ──────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScanning))]
    [NotifyPropertyChangedFor(nameof(IsServerListPhase))]
    [NotifyPropertyChangedFor(nameof(IsManualPhase))]
    [NotifyPropertyChangedFor(nameof(IsConnecting))]
    private ConnectPhase _phase = ConnectPhase.Scanning;

    public bool IsScanning => Phase == ConnectPhase.Scanning;
    public bool IsServerListPhase => Phase == ConnectPhase.ServerList;
    public bool IsManualPhase => Phase == ConnectPhase.ManualEntry;
    public bool IsConnecting => Phase == ConnectPhase.Connecting;

    // ──────────────────────────────────────────────
    // Server list
    // ──────────────────────────────────────────────
    public ObservableCollection<ServerInfo> Servers { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectToSelectedCommand))]
    private ServerInfo? _selectedServer;

    // ──────────────────────────────────────────────
    // Manual entry fields
    // ──────────────────────────────────────────────
    [ObservableProperty]
    private string _manualIp = string.Empty;

    [ObservableProperty]
    private string _manualPort = "5000";

    // ──────────────────────────────────────────────
    // Shared fields
    // ──────────────────────────────────────────────
    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Scanning your network for servers…";

    // ──────────────────────────────────────────────
    // Avatar
    // ──────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvatar))]
    private string? _avatarPath;

    public bool HasAvatar => !string.IsNullOrWhiteSpace(AvatarPath) && File.Exists(AvatarPath);

    // ──────────────────────────────────────────────
    // Constructor
    // ──────────────────────────────────────────────
    public ConnectViewModel(IChatService chatService)
    {
        _chatService = chatService;
        // Start scanning automatically when the view model is created
        _ = StartScanAsync();
    }

    // ──────────────────────────────────────────────
    // Commands
    // ──────────────────────────────────────────────

    /// <summary>Re-run the auto-scanner.</summary>
    [RelayCommand]
    private async Task RescanAsync()
    {
        _scanCts?.Cancel();
        Servers.Clear();
        SelectedServer = null;
        Phase = ConnectPhase.Scanning;
        StatusMessage = "Scanning your network for servers…";
        ErrorMessage = string.Empty;
        await StartScanAsync();
    }

    /// <summary>Connect to the currently selected server from the list.</summary>
    [RelayCommand(CanExecute = nameof(CanConnectToSelected))]
    private async Task ConnectToSelectedAsync()
    {
        if (SelectedServer == null) return;
        await DoConnectAsync(SelectedServer.Ip, SelectedServer.Port);
    }

    private bool CanConnectToSelected() => SelectedServer != null;

    /// <summary>Switch to manual IP entry form.</summary>
    [RelayCommand]
    private void EnterManually()
    {
        ErrorMessage = string.Empty;
        Phase = ConnectPhase.ManualEntry;
    }

    /// <summary>Go back to the server list from manual entry.</summary>
    [RelayCommand]
    private void BackToList()
    {
        ErrorMessage = string.Empty;
        Phase = ConnectPhase.ServerList;
    }

    /// <summary>Connect using the manually entered IP / port.</summary>
    [RelayCommand]
    private async Task ConnectManuallyAsync()
    {
        if (string.IsNullOrWhiteSpace(ManualIp))
        {
            ErrorMessage = "Please enter a server IP address.";
            return;
        }

        if (!int.TryParse(ManualPort, out int port) || port < 1 || port > 65535)
        {
            ErrorMessage = "Port must be a number between 1 and 65535.";
            return;
        }

        await DoConnectAsync(ManualIp.Trim(), port);
    }

    /// <summary>Open a file picker for avatar selection.</summary>
    [RelayCommand]
    private void PickAvatar()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choose your avatar",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp",
            Multiselect = false
        };

        if (dlg.ShowDialog() == true)
        {
            AvatarPath = dlg.FileName;
        }
    }

    /// <summary>Clear the chosen avatar.</summary>
    [RelayCommand]
    private void ClearAvatar()
    {
        AvatarPath = null;
    }

    // ──────────────────────────────────────────────
    // Internal helpers
    // ──────────────────────────────────────────────

    private async Task StartScanAsync()
    {
        _scanCts = new CancellationTokenSource();

        try
        {
            var servers = await Services.ServerDiscoveryService.ScanAsync(_scanCts.Token);

            Application.Current.Dispatcher.Invoke(() =>
            {
                Servers.Clear();
                foreach (var s in servers)
                    Servers.Add(s);

                if (Servers.Count > 0)
                {
                    SelectedServer = Servers[0];
                    StatusMessage = $"Found {Servers.Count} server{(Servers.Count == 1 ? "" : "s")} on your network.";
                }
                else
                {
                    StatusMessage = "No servers found on your network.";
                }

                Phase = ConnectPhase.ServerList;
            });
        }
        catch (OperationCanceledException)
        {
            // Scan was cancelled (e.g. user hit Rescan); ignore
        }
        catch (Exception ex)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                StatusMessage = "Scan failed. You can enter an IP manually.";
                ErrorMessage = ex.Message;
                Phase = ConnectPhase.ServerList;
            });
        }
    }

    private async Task DoConnectAsync(string ip, int port)
    {
        ErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(Username))
        {
            ErrorMessage = "Username is required.";
            return;
        }

        Phase = ConnectPhase.Connecting;
        StatusMessage = $"Connecting to {ip}:{port}…";

        try
        {
            await _chatService.ConnectAsync(ip, port, Username.Trim());

            // Signal successful connection — MainViewModel will switch the view
            WeakReferenceMessenger.Default.Send(new ConnectionSuccessMessage(Username.Trim(), AvatarPath));
        }
        catch (System.Net.Sockets.SocketException)
        {
            ErrorMessage = "Cannot connect. Check the server IP, your Wi-Fi, and that ChatServer is running.";
            Phase = ConnectPhase.ServerList;
        }
        catch (TimeoutException)
        {
            ErrorMessage = "Connection timed out. Make sure you're on the same Wi-Fi network.";
            Phase = ConnectPhase.ServerList;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
            Phase = ConnectPhase.ServerList;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Connection failed: {ex.Message}";
            Phase = ConnectPhase.ServerList;
        }
    }
}
