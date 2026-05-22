using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WpfChatClient.Services;

public class LanFileTransfer
{
    private const int SocketBufferSize = 1024 * 1024; // 1 MB
    private const int FileStreamBufferSize = 65536;

    /// <summary>
    /// SENDER: Listens for an incoming connection and sends the file using zero-copy.
    /// Returns the assigned port that the receiver must connect to.
    /// </summary>
    public static int HostFileZeroCopy(string filePath, out Task hostingTask, Action? onTransferStarted = null, Action? onTransferCompleted = null, CancellationToken cancellationToken = default)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("Source file does not exist.", filePath);

        long fileSize = fileInfo.Length;
        string fileName = fileInfo.Name;

        Socket listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Any, 0)); // OS assigns a random port
        listener.Listen(1);
        
        int assignedPort = ((IPEndPoint)listener.LocalEndPoint!).Port;

        hostingTask = Task.Run(async () =>
        {
            try
            {
                using (listener)
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var clientSocket = await listener.AcceptAsync(cancellationToken);
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                onTransferStarted?.Invoke();
                                using (clientSocket)
                                {
                                    clientSocket.SendBufferSize = SocketBufferSize;
                                    clientSocket.NoDelay = true;

                                    byte[] fileNameBytes = Encoding.UTF8.GetBytes(fileName);
                                    int headerLength = 8 + fileNameBytes.Length;
                                    
                                    byte[] headerBuffer = new byte[4 + headerLength];
                                    BitConverter.TryWriteBytes(headerBuffer.AsSpan(0, 4), headerLength);
                                    BitConverter.TryWriteBytes(headerBuffer.AsSpan(4, 8), fileSize);
                                    fileNameBytes.CopyTo(headerBuffer, 12);

                                    await clientSocket.SendFileAsync(
                                        fileName: filePath,
                                        preBuffer: headerBuffer,
                                        postBuffer: null,
                                        flags: TransmitFileOptions.UseDefaultWorkerThread,
                                        cancellationToken: cancellationToken);

                                    clientSocket.Shutdown(SocketShutdown.Send);
                                }
                                onTransferCompleted?.Invoke();
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error sending file to client: {ex.Message}");
                            }
                        }, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Console.WriteLine($"Network error during file host: {ex.Message}");
            }
        }, cancellationToken);

        return assignedPort;
    }

    /// <summary>
    /// RECEIVER: Connects to the host and downloads the file efficiently.
    /// </summary>
    public static async Task DownloadFileHighPerformanceAsync(string hostIp, int hostPort, string saveDirectory, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        using Socket clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        clientSocket.ReceiveBufferSize = SocketBufferSize;
        clientSocket.NoDelay = true;

        await clientSocket.ConnectAsync(hostIp, hostPort, cancellationToken);
        
        await using NetworkStream networkStream = new NetworkStream(clientSocket, ownsSocket: false);

        byte[] lengthBuffer = new byte[4];
        await ReadExactBytesAsync(networkStream, lengthBuffer, cancellationToken);
        int headerLength = BitConverter.ToInt32(lengthBuffer, 0);

        byte[] headerBuffer = new byte[headerLength];
        await ReadExactBytesAsync(networkStream, headerBuffer, cancellationToken);

        long expectedFileSize = BitConverter.ToInt64(headerBuffer, 0);
        string fileName = Encoding.UTF8.GetString(headerBuffer, 8, headerLength - 8);

        string savePath = Path.Combine(saveDirectory, fileName);
        Directory.CreateDirectory(saveDirectory);

        await using FileStream fileStream = new FileStream(
            savePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: FileStreamBufferSize,
            options: FileOptions.Asynchronous);

        if (progress == null)
        {
            await networkStream.CopyToAsync(fileStream, cancellationToken);
        }
        else
        {
            byte[] buffer = new byte[FileStreamBufferSize];
            long totalRead = 0;
            int read;
            while ((read = await networkStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                totalRead += read;
                double percent = expectedFileSize > 0 ? (totalRead * 100d / expectedFileSize) : 0;
                progress.Report(percent);
            }
        }
        
        await fileStream.FlushAsync(cancellationToken);

        if (fileStream.Length != expectedFileSize)
        {
            throw new IOException($"Transfer incomplete. Expected {expectedFileSize} bytes, but received {fileStream.Length}.");
        }
    }

    private static async Task ReadExactBytesAsync(NetworkStream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int totalRead = 0;
        int bytesToRead = buffer.Length;

        while (totalRead < bytesToRead)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead, bytesToRead - totalRead), cancellationToken);
            if (read == 0)
                throw new EndOfStreamException("Connection closed prematurely while reading stream.");
            
            totalRead += read;
        }
    }
}

