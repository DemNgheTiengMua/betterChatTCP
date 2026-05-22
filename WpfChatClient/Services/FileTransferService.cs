using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using WpfChatClient.Core.Interfaces;

namespace WpfChatClient.Services;

public sealed class FileTransferService : IFileTransferService
{
    private const int BufferSize = 1_048_576;      // 1 MB chunk size
    private const int SocketBufferSize = 4_194_304; // 4 MB socket buffer
    private const long MaxFileSizeBytes = 10L * 1024 * 1024 * 1024;

    public async Task UploadFileAsync(
        FileUploadRequest request,
        IProgress<FileTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateEndpoint(request.ServerIp, request.FilePort);
        ValidateRequired(request.TransferId, nameof(request.TransferId));
        ValidateRequired(request.FilePath, nameof(request.FilePath));

        var fileInfo = new FileInfo(request.FilePath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("Source file was not found.", request.FilePath);
        }

        ValidateFileSize(fileInfo.Length);
        if (request.FileSize != fileInfo.Length)
        {
            throw new InvalidOperationException("File size changed before upload started.");
        }

        string fileName = string.IsNullOrWhiteSpace(request.FileName) ? fileInfo.Name : request.FileName;

        using var client = new TcpClient { NoDelay = true, SendBufferSize = SocketBufferSize, ReceiveBufferSize = SocketBufferSize };
        await client.ConnectAsync(request.ServerIp, request.FilePort, cancellationToken).ConfigureAwait(false);

        NetworkStream networkStream = client.GetStream();
        await FileTransferProtocol.WriteJsonLineAsync(
            networkStream,
            new FilePortRequest
            {
                Type = "upload",
                TransferId = request.TransferId,
                Sender = request.Sender,
                RoomId = request.RoomId,
                FileName = fileName,
                FileSize = fileInfo.Length
            },
            cancellationToken).ConfigureAwait(false);

        FilePortResponse response = await ReadResponseAsync(networkStream, cancellationToken).ConfigureAwait(false);
        EnsureAccepted(response, "File upload was rejected by the server.");

        // 2. Zero-Copy Sending
        // Let the OS handle the zero-copy transfer directly from disk to the socket
        await client.Client.SendFileAsync(
            fileName: request.FilePath,
            preBuffer: null,
            postBuffer: null,
            flags: TransmitFileOptions.UseDefaultWorkerThread,
            cancellationToken: cancellationToken);

        // Final 100% progress report since zero-copy bypasses user space
        double percent = fileInfo.Length <= 0 ? 0 : 100;
        progress?.Report(new FileTransferProgress(fileInfo.Length, fileInfo.Length, percent));
    }

    public async Task DownloadFileAsync(
        FileDownloadRequest request,
        IProgress<FileTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateEndpoint(request.ServerIp, request.FilePort);
        ValidateRequired(request.TransferId, nameof(request.TransferId));
        ValidateRequired(request.DestinationPath, nameof(request.DestinationPath));

        string partPath = request.DestinationPath + ".part";

        try
        {
            string? directory = Path.GetDirectoryName(request.DestinationPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var client = new TcpClient { NoDelay = true, SendBufferSize = SocketBufferSize, ReceiveBufferSize = SocketBufferSize };
            await client.ConnectAsync(request.ServerIp, request.FilePort, cancellationToken).ConfigureAwait(false);

            NetworkStream networkStream = client.GetStream();
            await FileTransferProtocol.WriteJsonLineAsync(
                networkStream,
                new FilePortRequest
                {
                    Type = "download",
                    TransferId = request.TransferId,
                    Requester = request.Requester,
                    RoomId = request.RoomId
                },
                cancellationToken).ConfigureAwait(false);

            FilePortResponse response = await ReadResponseAsync(networkStream, cancellationToken).ConfigureAwait(false);
            EnsureAccepted(response, "File download was rejected by the server.");

            long expectedBytes = response.FileSize;
            ValidateFileSize(expectedBytes);

            await using (var output = new FileStream(
                partPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 65536, // FileStream Optimization: at least 65536
                useAsync: true))
            {
                output.SetLength(expectedBytes); // Pre-allocate file size
                
                // 3. High-Performance Receiving: Use CopyToAsync
                await networkStream.CopyToAsync(output, cancellationToken).ConfigureAwait(false);

                // Final 100% progress report
                double percent = expectedBytes <= 0 ? 0 : 100;
                progress?.Report(new FileTransferProgress(expectedBytes, expectedBytes, percent));

                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(partPath, request.DestinationPath, overwrite: true);
        }
        catch
        {
            SafeDelete(partPath);
            throw;
        }
    }

    private static async Task<FilePortResponse> ReadResponseAsync(NetworkStream networkStream, CancellationToken cancellationToken)
    {
        FilePortResponse? response = await FileTransferProtocol.ReadJsonLineAsync<FilePortResponse>(networkStream, cancellationToken)
            .ConfigureAwait(false);

        if (response == null)
        {
            throw new IOException("Server closed the connection before sending a response.");
        }

        return response;
    }

    private static void EnsureAccepted(FilePortResponse response, string defaultMessage)
    {
        if (response.IsSuccess)
        {
            return;
        }

        string message = string.IsNullOrWhiteSpace(response.ErrorText) ? defaultMessage : response.ErrorText;
        throw new InvalidOperationException(message);
    }

    private static void ValidateEndpoint(string serverIp, int filePort)
    {
        ValidateRequired(serverIp, nameof(serverIp));
        if (filePort <= 0 || filePort > 65_535)
        {
            throw new ArgumentOutOfRangeException(nameof(filePort), "File port must be between 1 and 65535.");
        }
    }

    private static void ValidateRequired(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", name);
        }
    }

    private static void ValidateFileSize(long fileSize)
    {
        if (fileSize < 1)
        {
            throw new InvalidOperationException("File must contain at least one byte.");
        }

        if (fileSize > MaxFileSizeBytes)
        {
            throw new InvalidOperationException("File exceeds the 10 GB size limit.");
        }
    }

    private static void ReportProgressThrottled(
        ref long lastReportedBytes,
        ref long lastReportTime,
        long transferred,
        long totalBytes,
        long minBytesInterval,
        long minTimeIntervalTicks,
        IProgress<FileTransferProgress>? progress)
    {
        long currentTime = System.Diagnostics.Stopwatch.GetTimestamp();
        long elapsedTicks = currentTime - lastReportTime;
        long bytesSinceLastReport = transferred - lastReportedBytes;

        if (transferred == totalBytes || bytesSinceLastReport >= minBytesInterval || elapsedTicks >= minTimeIntervalTicks)
        {
            lastReportedBytes = transferred;
            lastReportTime = currentTime;
            double percent = totalBytes <= 0 ? 0 : Math.Min(100, transferred * 100d / totalBytes);
            progress?.Report(new FileTransferProgress(transferred, totalBytes, percent));
        }
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
