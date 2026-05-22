namespace WpfChatClient.Models;
public record User(string Name, string Color, string Status = "Online", bool IsTyping = false, string? AvatarPath = null)
{
    public bool HasAvatar => !string.IsNullOrWhiteSpace(AvatarPath) && System.IO.File.Exists(AvatarPath);
    public string Initials => Name.Length > 0 ? Name[..1].ToUpper() : "?";
}
