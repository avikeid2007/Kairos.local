using CommunityToolkit.Mvvm.ComponentModel;

namespace KaiROS.Mobile.Models;

/// <summary>
/// Represents a chat message in the conversation.
/// Uses ObservableObject for streaming updates.
/// </summary>
public partial class ChatMessage : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    [ObservableProperty]
    private ChatRole _role;
    
    [ObservableProperty]
    private string _content = string.Empty;
    
    public DateTime Timestamp { get; set; } = DateTime.Now;
    
    [ObservableProperty]
    private bool _isStreaming;
}

/// <summary>
/// Role of the message sender.
/// </summary>
public enum ChatRole
{
    System,
    User,
    Assistant
}
