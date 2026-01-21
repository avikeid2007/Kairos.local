using CommunityToolkit.Mvvm.ComponentModel;
using SQLite;

namespace KaiROS.Mobile.Models;

/// <summary>
/// Represents a chat message in the conversation.
/// Uses ObservableObject for streaming updates.
/// </summary>
[Table("ChatMessages")]
public partial class ChatMessage : ObservableObject
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Indexed]
    public int SessionId { get; set; }

    // Role stored as int for SQLite
    public int RoleValue { get; set; }

    [Ignore]
    public ChatRole Role
    {
        get => (ChatRole)RoleValue;
        set
        {
            RoleValue = (int)value;
            OnPropertyChanged();
        }
    }

    // Content stored for SQLite - this is the actual backing store
    [ObservableProperty]
    private string _contentValue = string.Empty;

    // When ContentValue changes, also notify that Content changed (since UI binds to Content)
    partial void OnContentValueChanged(string value)
    {
        OnPropertyChanged(nameof(Content));
    }

    // Content is the UI-facing property that redirects to ContentValue
    [Ignore]
    public string Content
    {
        get => ContentValue;
        set => ContentValue = value;
    }

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
