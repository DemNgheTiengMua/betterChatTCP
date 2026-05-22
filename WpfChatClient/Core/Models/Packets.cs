using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WpfChatClient.Core.Models;

public enum PacketType
{
    Join,
    Leave,
    ChatMessage,
    Typing,
    UserListUpdate,
    SystemMessage,
    Heartbeat,
    PrivateMessage,
    RoomJoin,
    ConnectionRejected,
    FileOffer,
    FileAvailable,
    FileTransferFailed,
    FileUploadProgress,
    FileListRequest,
    FileListResponse
}

public class Packet
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PacketType Type { get; set; }
    public JsonElement Data { get; set; }
}

public class JoinData
{
    public string Username { get; set; } = string.Empty;
    public string? AvatarBase64 { get; set; }
}

public class ChatMessageData
{
    public string MessageId { get; set; } = string.Empty;
    public string RoomId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
}

public class PrivateMessageData
{
    public string MessageId { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
    public string Recipient { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
}

public class RoomJoinData
{
    public string RoomId { get; set; } = string.Empty;
}

public class TypingData
{
    public string Username { get; set; } = string.Empty;
    public bool IsTyping { get; set; }
}

public class UserInfoData
{
    public string Username { get; set; } = string.Empty;
    public string? AvatarBase64 { get; set; }
}

public class UserListUpdateData
{
    public List<UserInfoData> Users { get; set; } = new();
}
public class SystemMessageData { public string Message { get; set; } = string.Empty; }
public class HeartbeatData { }

public class FileOfferData
{
    public string TransferId { get; set; } = string.Empty;
    public string RoomId { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}

public class FileAvailableData
{
    public string TransferId { get; set; } = string.Empty;
    public string RoomId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
}

public class FileTransferFailedData
{
    public string TransferId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class FileUploadProgressData
{
    public string TransferId { get; set; } = string.Empty;
    public string RoomId { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
    public long BytesReceived { get; set; }
    public long TotalBytes { get; set; }
    public double Percent { get; set; }
}

public class FileListRequestData
{
    public string RoomId { get; set; } = string.Empty;
}

public class FileItemData
{
    public string TransferId { get; set; } = string.Empty;
    public string RoomId { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}

public class FileListResponseData
{
    public string RoomId { get; set; } = string.Empty;
    public List<FileItemData> Files { get; set; } = new();
}

