namespace WpfChatClient.Models;
public record User(string Name, string Color, string Status = "Online", bool IsTyping = false, string? AvatarPath = null);
