namespace WpfChatClient.Messages;

public class ConnectionSuccessMessage
{
    public string Username { get; }
    public string? AvatarPath { get; }

    public ConnectionSuccessMessage(string username, string? avatarPath)
    {
        Username = username;
        AvatarPath = avatarPath;
    }
}
