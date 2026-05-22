using System;
namespace WpfChatClient.Models;

public record ChatMessage(
    string Sender,
    string Content,
    string Time,
    bool IsOwn,
    string MessageId = "",
    string AvatarColor = "#5865F2",
    bool IsSticker = false,
    string StickerId = "",
    string StickerGlyph = "",
    string StickerName = "",
    string StickerAccentColor = "#5865F2",
    bool IsFile = false,
    FileTransferItem? FileTransfer = null,
    bool IsImage = false,
    string ImagePath = "")
{
    public bool IsText => !IsSticker && !IsFile && !IsImage;
}
