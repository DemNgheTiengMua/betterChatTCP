using System;
using System.Threading;
using System.Threading.Tasks;

namespace WpfChatClient.Core.Interfaces;

public sealed record FileUploadRequest(
    string ServerIp,
    int FilePort,
    string TransferId,
    string Sender,
    string RoomId,
    string FilePath,
    string FileName,
    long FileSize);

public sealed record FileDownloadRequest(
    string ServerIp,
    int FilePort,
    string TransferId,
    string Requester,
    string RoomId,
    string DestinationPath);

public sealed record FileTransferProgress(
    long BytesTransferred,
    long TotalBytes,
    double Percent);

public interface IFileTransferService
{
    Task UploadFileAsync(
        FileUploadRequest request,
        IProgress<FileTransferProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task DownloadFileAsync(
        FileDownloadRequest request,
        IProgress<FileTransferProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
