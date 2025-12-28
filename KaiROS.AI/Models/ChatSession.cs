namespace KaiROS.AI.Models;

/// <summary>
/// Represents a chat session stored in the database
/// </summary>
public class ChatSession
{
    public int Id { get; set; }
    public string Title { get; set; } = "New Chat";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string? ModelName { get; set; }
    public string? SystemPrompt { get; set; }
    public int MessageCount { get; set; }
    
    // Not stored in DB, loaded separately
    public List<ChatMessage> Messages { get; set; } = new();
    
    /// <summary>
    /// Generate a title from the first user message
    /// </summary>
    public static string GenerateTitle(string firstMessage)
    {
        if (string.IsNullOrWhiteSpace(firstMessage))
            return "New Chat";
            
        // Take first 50 characters or first sentence
        var title = firstMessage.Trim();
        var sentenceEnd = title.IndexOfAny(new[] { '.', '!', '?', '\n' });
        if (sentenceEnd > 0 && sentenceEnd < 50)
            title = title[..sentenceEnd];
        else if (title.Length > 50)
            title = title[..47] + "...";
            
        return title;
    }
}
