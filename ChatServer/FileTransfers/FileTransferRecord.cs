namespace ChatServer.FileTransfers;

public sealed class FileTransferRecord
{
    public required string TransferId { get; init; }
    public required string SafeTransferId { get; init; }
    public required string RoomId { get; init; }
    public required string Sender { get; init; }
    public required string SenderAddress { get; init; }
    public required string OriginalFileName { get; init; }
    public required string SafeFileName { get; init; }
    public long FileSize { get; set; }
    public string P2PAddress { get; set; } = string.Empty;
    public int P2PPort { get; set; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? AvailableUtc { get; set; }
    public DateTime? FailedUtc { get; set; }
    public string? FinalPath { get; set; }
    public string? FailureReason { get; set; }
    public FileTransferStatus Status { get; set; } = FileTransferStatus.Pending;
}
