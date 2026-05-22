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
    // Requirement 5: Large Socket buffers (1 MB) to maximize LAN sliding window throughput
    private const int SocketBufferSize = 1024 * 1024; 
    
    // Requirement 4: At least 64 KB FileStream buffer
    private const int FileStreamBufferSize = 65536;

    /// <summary>
    /// SENDER: Uses zero-copy OS-level transmission.
    /// </summary>
    public static async Task SendFileZeroCopyAsync(string filePath, string targetIp, int targetPort, CancellationToken cancellationToken = default)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
                throw new FileNotFoundException("Source file does not exist.", filePath);

            long fileSize = fileInfo.Length;
            string fileName = fileInfo.Name;

            // 1. Dedicated TCP Connection
            using Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            
            // 5. Explicit Buffer Tuning & NoDelay for reduced latency
            socket.SendBufferSize = SocketBufferSize;
            socket.ReceiveBufferSize = SocketBufferSize;
            socket.NoDelay = true; 

            await socket.ConnectAsync(targetIp, targetPort, cancellationToken);

            // 6. Metadata Header
            // Format: [HeaderLength (4 bytes)] [FileSize (8 bytes)] [FileName UTF8 bytes]
            byte[] fileNameBytes = Encoding.UTF8.GetBytes(fileName);
            int headerLength = 8 + fileNameBytes.Length;
            
            byte[] headerBuffer = new byte[4 + headerLength];
            BitConverter.TryWriteBytes(headerBuffer.AsSpan(0, 4), headerLength);
            BitConverter.TryWriteBytes(headerBuffer.AsSpan(4, 8), fileSize);
            fileNameBytes.CopyTo(headerBuffer, 12);

            // 2. Zero-Copy Sending
            // SENIOR TIP: By passing the headerBuffer as the "preBuffer" to SendFileAsync, 
            // the OS kernel will combine your metadata header and the zero-copy disk read 
            // into a single TransmitFile system call!
            await socket.SendFileAsync(
                fileName: filePath,
                preBuffer: headerBuffer,
                postBuffer: null,
                flags: TransmitFileOptions.UseDefaultWorkerThread,
                cancellationToken: cancellationToken);

            // Gracefully send FIN packet. This tells the receiver's NetworkStream
            // that no more data is coming, allowing their CopyToAsync to naturally complete.
            socket.Shutdown(SocketShutdown.Send);
        }
        catch (SocketException ex)
        {
            // Logging or higher-level exception propagation here
            Console.WriteLine($"Network error during file send: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending file: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// RECEIVER: Highly optimized stream copying.
    /// </summary>
    public static async Task ReceiveFileHighPerformanceAsync(int listeningPort, string saveDirectory, CancellationToken cancellationToken = default)
    {
        // 1. Dedicated TCP listener for this specific transfer
        using Socket listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            listener.ReceiveBufferSize = SocketBufferSize;
            listener.SendBufferSize = SocketBufferSize;
            listener.Bind(new IPEndPoint(IPAddress.Any, listeningPort));
            listener.Listen(1); // Backlog of 1 for a 1-to-1 dedicated transfer

            using Socket clientSocket = await listener.AcceptAsync(cancellationToken);
            
            // 5. Buffer tuning on the accepted socket
            clientSocket.ReceiveBufferSize = SocketBufferSize;
            clientSocket.SendBufferSize = SocketBufferSize;
            clientSocket.NoDelay = true;

            await using NetworkStream networkStream = new NetworkStream(clientSocket, ownsSocket: false);

            // Read Metadata Header Length (4 bytes)
            byte[] lengthBuffer = new byte[4];
            await ReadExactBytesAsync(networkStream, lengthBuffer, cancellationToken);
            int headerLength = BitConverter.ToInt32(lengthBuffer, 0);

            // Read the rest of the Header
            byte[] headerBuffer = new byte[headerLength];
            await ReadExactBytesAsync(networkStream, headerBuffer, cancellationToken);

            // 6. Decode Header
            long expectedFileSize = BitConverter.ToInt64(headerBuffer, 0);
            string fileName = Encoding.UTF8.GetString(headerBuffer, 8, headerLength - 8);

            string savePath = Path.Combine(saveDirectory, fileName);
            Directory.CreateDirectory(saveDirectory);

            // 4. FileStream Optimization (Asynchronous + 64KB Buffer)
            await using FileStream fileStream = new FileStream(
                savePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: FileStreamBufferSize,
                options: FileOptions.Asynchronous);

            // 3. High-Performance Receiving
            // Because the sender executes a clean Socket.Shutdown, this single line cleanly reads the stream exactly until EOF.
            await networkStream.CopyToAsync(fileStream, cancellationToken);
            await fileStream.FlushAsync(cancellationToken);

            // Validate the payload size matched what the metadata told us to expect
            if (fileStream.Length != expectedFileSize)
            {
                throw new IOException($"Transfer incomplete. Expected {expectedFileSize} bytes, but received {fileStream.Length}.");
            }
        }
        catch (SocketException ex)
        {
            Console.WriteLine($"Network error during file receive: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error receiving file: {ex.Message}");
            throw;
        }
    }

    // Helper to ensure we don't accidentally under-read TCP fragments while reading the header
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
