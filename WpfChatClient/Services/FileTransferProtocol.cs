using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace WpfChatClient.Services;

public sealed record FilePortRequest
{
    public string Type { get; init; } = string.Empty;
    public string TransferId { get; init; } = string.Empty;
    public string Sender { get; init; } = string.Empty;
    public string RoomId { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public string Requester { get; init; } = string.Empty;
}

public sealed record FilePortResponse
{
    public bool Ok { get; init; }
    public bool Success { get; init; }
    public bool Accepted { get; init; }
    public string Error { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public long FileSize { get; init; }

    [JsonIgnore]
    public bool IsSuccess => Ok || Success || Accepted;

    [JsonIgnore]
    public string ErrorText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Error))
            {
                return Error;
            }

            return string.IsNullOrWhiteSpace(ErrorMessage) ? Message : ErrorMessage;
        }
    }
}

public static class FileTransferProtocol
{
    private const int MaxJsonLineBytes = 16_384;
    private static readonly byte[] NewLine = [(byte)'\n'];

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static async Task<T?> ReadJsonLineAsync<T>(Stream stream, CancellationToken cancellationToken = default)
    {
        var bytes = new List<byte>();
        var buffer = new byte[1];

        while (true)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (bytes.Count == 0)
                {
                    return default;
                }

                break;
            }

            byte value = buffer[0];
            if (value == (byte)'\n')
            {
                break;
            }

            if (value == (byte)'\r')
            {
                continue;
            }

            bytes.Add(value);
            if (bytes.Count > MaxJsonLineBytes)
            {
                throw new InvalidDataException($"JSON header exceeds {MaxJsonLineBytes} bytes.");
            }
        }

        string json = Encoding.UTF8.GetString(bytes.ToArray());
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    public static async Task WriteJsonLineAsync<T>(Stream stream, T value, CancellationToken cancellationToken = default)
    {
        string json = JsonSerializer.Serialize(value, JsonOptions);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > MaxJsonLineBytes)
        {
            throw new InvalidDataException($"JSON header exceeds {MaxJsonLineBytes} bytes.");
        }

        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(NewLine, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
