using System.Net;
using System.Net.Sockets;

namespace ChatServer.FileTransfers;

public sealed class FileTransferServer
{
    private const int SocketBufferSize = 4_194_304; // 4 MB
    private readonly int _port;
    private readonly FileMetadataStore _metadataStore;
    private readonly Func<string, string, string, bool> _isUserInRoomFromAddress;
    private readonly Func<FileTransferRecord, Task> _onAvailable;
    private readonly Func<FileTransferRecord, Task> _onFailed;
    private readonly Func<FileTransferRecord, long, Task>? _onProgress;
    private TcpListener? _listener;

    public FileTransferServer(
        int port,
        FileMetadataStore metadataStore,
        Func<string, string, string, bool> isUserInRoomFromAddress,
        Func<FileTransferRecord, Task> onAvailable,
        Func<FileTransferRecord, Task> onFailed,
        Func<FileTransferRecord, long, Task>? onProgress = null)
    {
        _port = port;
        _metadataStore = metadataStore;
        _isUserInRoomFromAddress = isUserInRoomFromAddress;
        _onAvailable = onAvailable;
        _onFailed = onFailed;
        _onProgress = onProgress;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _listener = new TcpListener(IPAddress.Any, _port);

        try
        {
            _listener.Start();
            Console.WriteLine($"[FILE] server listening on TCP port {_port}");

            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                client.SendBufferSize = SocketBufferSize;
                client.ReceiveBufferSize = SocketBufferSize;
                Console.WriteLine("[FILE] File transfer connection accepted.");

                var session = new FileTransferSession(client, _metadataStore, _isUserInRoomFromAddress, _onAvailable, _onFailed, _onProgress);
                _ = Task.Run(() => session.RunAsync(cancellationToken), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FILE] File transfer server stopped after error: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
        finally
        {
            try
            {
                _listener.Stop();
            }
            catch
            {
            }
        }
    }
}
