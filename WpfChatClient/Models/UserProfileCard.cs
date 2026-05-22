namespace WpfChatClient.Models;

public record UserProfileCard(
    string Name,
    string AvatarColor,
    string Status,
    bool IsTyping,
    string? AvatarPath = null);
