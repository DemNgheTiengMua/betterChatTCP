namespace WpfChatClient.Models;

public enum FileTransferUiStatus
{
    Pending,
    Uploading,
    Available,
    Downloading,
    Downloaded,
    Failed,
    Canceled
}
