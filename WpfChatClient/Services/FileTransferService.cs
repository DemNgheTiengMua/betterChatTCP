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

        await using var fileStream = new FileStream(
            request.FilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            useAsync: true);

        var bufferA = new byte[BufferSize];
        var bufferB = new byte[BufferSize];
        byte[] currentReadBuffer = bufferA;
        byte[] currentWriteBuffer = bufferB;

        long transferred = 0;
        
        long lastReportedBytes = 0;
        long lastReportTime = System.Diagnostics.Stopwatch.GetTimestamp();
        long minBytesInterval = Math.Max(65536, fileInfo.Length / 200); // 0.5% steps
        long ticksPerMs = System.Diagnostics.Stopwatch.Frequency / 1000;
        long minTimeIntervalTicks = ticksPerMs * 100; // 100ms interval

        ReportProgressThrottled(ref lastReportedBytes, ref lastReportTime, transferred, fileInfo.Length, minBytesInterval, minTimeIntervalTicks, progress);

        Task<int> readTask = fileStream.ReadAsync(currentReadBuffer.AsMemory(0, currentReadBuffer.Length), cancellationToken).AsTask();
        Task writeTask = Task.CompletedTask;

        int bytesRead = await readTask.ConfigureAwait(false);
        while (bytesRead > 0)
        {
            await writeTask.ConfigureAwait(false);

            int bytesToWrite = bytesRead;
            byte[] bufferToWrite = currentReadBuffer;
            byte[] nextReadBuffer = currentWriteBuffer;

            writeTask = networkStream.WriteAsync(bufferToWrite.AsMemory(0, bytesToWrite), cancellationToken).AsTask();

            currentReadBuffer = nextReadBuffer;
            currentWriteBuffer = bufferToWrite;

            transferred += bytesToWrite;
            ReportProgressThrottled(ref lastReportedBytes, ref lastReportTime, transferred, fileInfo.Length, minBytesInterval, minTimeIntervalTicks, progress);

            readTask = fileStream.ReadAsync(currentReadBuffer.AsMemory(0, currentReadBuffer.Length), cancellationToken).AsTask();
            bytesRead = await readTask.ConfigureAwait(false);
        }
        await writeTask.ConfigureAwait(false);

        // Final 100% progress report
        double percent = fileInfo.Length <= 0 ? 0 : 100;
        progress?.Report(new FileTransferProgress(fileInfo.Length, fileInfo.Length, percent));

        await networkStream.FlushAsync(cancellationToken).ConfigureAwait(false);
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
                BufferSize,
                useAsync: true))
            {
                output.SetLength(expectedBytes); // Pre-allocate file size
                var bufferA = new byte[BufferSize];
                var bufferB = new byte[BufferSize];
                byte[] currentReadBuffer = bufferA;
                byte[] currentWriteBuffer = bufferB;

                long transferred = 0;

                long lastReportedBytes = 0;
                long lastReportTime = System.Diagnostics.Stopwatch.GetTimestamp();
                long minBytesInterval = Math.Max(65536, expectedBytes / 200); // 0.5% steps
                long ticksPerMs = System.Diagnostics.Stopwatch.Frequency / 1000;
                long minTimeIntervalTicks = ticksPerMs * 100; // 100ms interval

                ReportProgressThrottled(ref lastReportedBytes, ref lastReportTime, transferred, expectedBytes, minBytesInterval, minTimeIntervalTicks, progress);

                long remaining = expectedBytes;
                int bytesToRead = (int)Math.Min(currentReadBuffer.Length, remaining);
                Task<int> readTask = networkStream.ReadAsync(currentReadBuffer.AsMemory(0, bytesToRead), cancellationToken).AsTask();
                Task writeTask = Task.CompletedTask;

                int bytesRead = await readTask.ConfigureAwait(false);
                if (bytesRead == 0 && remaining > 0)
                {
                    throw new EndOfStreamException("Server closed the connection before the file was fully downloaded.");
                }

                while (bytesRead > 0)
                {
                    await writeTask.ConfigureAwait(false);

                    int bytesToWrite = bytesRead;
                    byte[] bufferToWrite = currentReadBuffer;
                    byte[] nextReadBuffer = currentWriteBuffer;

                    writeTask = output.WriteAsync(bufferToWrite.AsMemory(0, bytesToWrite), cancellationToken).AsTask();

                    currentReadBuffer = nextReadBuffer;
                    currentWriteBuffer = bufferToWrite;

                    remaining -= bytesToWrite;
                    transferred += bytesToWrite;
                    ReportProgressThrottled(ref lastReportedBytes, ref lastReportTime, transferred, expectedBytes, minBytesInterval, minTimeIntervalTicks, progress);

                    if (remaining <= 0)
                    {
                        bytesRead = 0;
                        break;
                    }

                    bytesToRead = (int)Math.Min(currentReadBuffer.Length, remaining);
                    readTask = networkStream.ReadAsync(currentReadBuffer.AsMemory(0, bytesToRead), cancellationToken).AsTask();
                    bytesRead = await readTask.ConfigureAwait(false);
                    if (bytesRead == 0 && remaining > 0)
                    {
                        await writeTask.ConfigureAwait(false);
                        throw new EndOfStreamException("Server closed the connection before the file was fully downloaded.");
                    }
                }
                await writeTask.ConfigureAwait(false);

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
