using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ChatServer.FileTransfers;

public sealed class FilePortRequest
{
    public string Type { get; set; } = string.Empty;
    public string TransferId { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
    public string RoomId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Requester { get; set; } = string.Empty;
}

public sealed class FilePortResponse
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public string? TransferId { get; set; }
    public string? FileName { get; set; }
    public long? FileSize { get; set; }
}

public static class FileTransferProtocol
{
    private const int MaxJsonLineBytes = 16_384;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static async Task<string?> ReadJsonLineAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new List<byte>();
        var singleByte = new byte[1];

        while (true)
        {
            var bytesRead = await stream.ReadAsync(singleByte.AsMemory(0, 1), cancellationToken);
            if (bytesRead == 0)
            {
                return null;
            }

            var value = singleByte[0];
            if (value == '\n')
            {
                return Encoding.UTF8.GetString(buffer.ToArray());
            }

            if (value == '\r')
            {
                continue;
            }

            buffer.Add(value);
            if (buffer.Count > MaxJsonLineBytes)
            {
                throw new InvalidDataException($"JSON line exceeds {MaxJsonLineBytes} bytes.");
            }
        }
    }

    public static async Task WriteJsonLineAsync(
        NetworkStream stream,
        object value,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json + "\n");
        await stream.WriteAsync(bytes.AsMemory(0, bytes.Length), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
