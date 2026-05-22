using System;
using System.Diagnostics;
using System.IO;

namespace WpfChatClient.Services;

public class ServerHostService
{
    private Process? _serverProcess;

    /// <summary>
    /// Attempts to start the ChatServer process.
    /// Looks for the server executable relative to the client's location.
    /// </summary>
    public bool TryStartServer(out string errorMessage)
    {
        errorMessage = string.Empty;

        if (_serverProcess != null && !_serverProcess.HasExited)
        {
            errorMessage = "Server is already running.";
            return true; // Already running is still a success
        }

        // Look for the ChatServer executable in common locations
        var possiblePaths = new[]
        {
            // Same solution: sibling project output
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ChatServer", "bin", "Debug", "net9.0", "ChatServer.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ChatServer", "bin", "Release", "net9.0", "ChatServer.exe"),
            // Same directory
            Path.Combine(AppContext.BaseDirectory, "ChatServer.exe"),
            // Sibling directory
            Path.Combine(AppContext.BaseDirectory, "..", "ChatServer", "ChatServer.exe"),
        };

        string? serverPath = null;
        foreach (var path in possiblePaths)
        {
            var resolved = Path.GetFullPath(path);
            if (File.Exists(resolved))
            {
                serverPath = resolved;
                break;
            }
        }

        if (serverPath == null)
        {
            errorMessage = "Could not find ChatServer.exe. Make sure you've built the ChatServer project.";
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = serverPath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(serverPath)!
            };

            _serverProcess = Process.Start(psi);

            if (_serverProcess == null)
            {
                errorMessage = "Failed to start the server process.";
                return false;
            }

            Console.WriteLine($"[HOST] Started ChatServer (PID: {_serverProcess.Id}) at {serverPath}");
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to start server: {ex.Message}";
            return false;
        }
    }

    public bool IsServerRunning => _serverProcess != null && !_serverProcess.HasExited;

    public void StopServer()
    {
        try
        {
            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                _serverProcess.Kill();
                _serverProcess.Dispose();
                _serverProcess = null;
                Console.WriteLine("[HOST] Server process stopped.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HOST] Failed to stop server: {ex.Message}");
        }
    }
}
