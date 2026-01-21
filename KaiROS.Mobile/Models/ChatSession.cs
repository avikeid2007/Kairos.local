using SQLite;

namespace KaiROS.Mobile.Models;

/// <summary>
/// Represents a chat session/conversation stored in the database.
/// </summary>
[Table("ChatSessions")]
public class ChatSession
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    
    public string Title { get; set; } = "New Chat";
    
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    
    public DateTime LastModifiedAt { get; set; } = DateTime.Now;
    
    public string? ModelName { get; set; }
    
    public int MessageCount { get; set; }
}
