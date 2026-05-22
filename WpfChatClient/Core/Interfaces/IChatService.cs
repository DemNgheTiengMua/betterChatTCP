using System;
using System.Threading.Tasks;
using WpfChatClient.Core.Models;

namespace WpfChatClient.Core.Interfaces;

public delegate void MessageReceivedHandler(string sender, string time, string content, string messageId, string roomId);
public delegate void PrivateMessageReceivedHandler(string sender, string time, string content, string messageId);
public delegate void UsersUpdatedHandler(string[] users);
public delegate void UserTypingHandler(string username, bool isTyping);
public delegate void ConnectionStateHandler();
public delegate void FileOfferReceivedHandler(FileOfferData offer);
public delegate void FileAvailableReceivedHandler(FileAvailableData file);
public delegate void FileTransferFailedReceivedHandler(FileTransferFailedData failure);
public delegate void FileUploadProgressReceivedHandler(FileUploadProgressData progress);
public delegate void FileListResponseReceivedHandler(FileListResponseData response);

public interface IChatService
{
    event MessageReceivedHandler MessageReceived; // sender, time, content, messageId, roomId
    event PrivateMessageReceivedHandler PrivateMessageReceived; // sender, time, content, messageId
    event UsersUpdatedHandler UsersUpdated;
    event UserTypingHandler UserTyping;
    event ConnectionStateHandler ConnectionLost;
    event ConnectionStateHandler ConnectionRestored;
    event FileOfferReceivedHandler FileOfferReceived;
    event FileAvailableReceivedHandler FileAvailableReceived;
    event FileTransferFailedReceivedHandler FileTransferFailedReceived;
    event FileUploadProgressReceivedHandler FileUploadProgressReceived;
    event FileListResponseReceivedHandler FileListResponseReceived;
    
    string? CurrentUsername { get; }
    string? ServerIp { get; }
    int ServerPort { get; }
    int FilePort { get; }
    bool IsConnected { get; }
    Task ConnectAsync(string ip, int port, string username, string? avatarPath = null);
    Task JoinRoomAsync(string roomId, bool setActive = true);
    Task<string?> SendMessageAsync(string message);
    Task<bool> SendFileOfferAsync(FileOfferData offer);
    Task SendTypingAsync(bool isTyping);
    Task RequestFileListAsync(string roomId);
    void Disconnect();
}
