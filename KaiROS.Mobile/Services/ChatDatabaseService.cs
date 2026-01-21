using KaiROS.Mobile.Models;
using SQLite;
using System.Diagnostics;

namespace KaiROS.Mobile.Services;

/// <summary>
/// Service for managing chat history using SQLite database.
/// </summary>
public class ChatDatabaseService
{
    private SQLiteAsyncConnection? _database;
    private readonly string _databasePath;

    public ChatDatabaseService()
    {
        _databasePath = Path.Combine(FileSystem.AppDataDirectory, "chathistory.db");
    }

    private async Task InitAsync()
    {
        if (_database != null)
            return;

        _database = new SQLiteAsyncConnection(_databasePath);
        await _database.CreateTableAsync<ChatSession>();
        await _database.CreateTableAsync<ChatMessage>();
    }

    #region Sessions

    /// <summary>
    /// Get all chat sessions ordered by last modified date.
    /// </summary>
    public async Task<List<ChatSession>> GetAllSessionsAsync()
    {
        await InitAsync();
        return await _database!.Table<ChatSession>()
            .OrderByDescending(s => s.LastModifiedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Get a session by ID.
    /// </summary>
    public async Task<ChatSession?> GetSessionAsync(int sessionId)
    {
        await InitAsync();
        return await _database!.Table<ChatSession>()
            .FirstOrDefaultAsync(s => s.Id == sessionId);
    }

    /// <summary>
    /// Create a new chat session.
    /// </summary>
    public async Task<ChatSession> CreateSessionAsync(string? modelName = null)
    {
        await InitAsync();
        var session = new ChatSession
        {
            Title = "New Chat",
            CreatedAt = DateTime.Now,
            LastModifiedAt = DateTime.Now,
            ModelName = modelName
        };
        await _database!.InsertAsync(session);
        return session;
    }

    /// <summary>
    /// Update an existing session.
    /// </summary>
    public async Task UpdateSessionAsync(ChatSession session)
    {
        await InitAsync();
        session.LastModifiedAt = DateTime.Now;
        await _database!.UpdateAsync(session);
    }

    /// <summary>
    /// Delete a session and all its messages.
    /// </summary>
    public async Task DeleteSessionAsync(int sessionId)
    {
        await InitAsync();
        await _database!.Table<ChatMessage>()
            .DeleteAsync(m => m.SessionId == sessionId);
        await _database!.Table<ChatSession>()
            .DeleteAsync(s => s.Id == sessionId);
    }

    /// <summary>
    /// Purge sessions older than the retention period.
    /// </summary>
    public async Task<int> PurgeOldSessionsAsync(int retentionDays)
    {
        await InitAsync();
        var cutoffDate = DateTime.Now.AddDays(-retentionDays);
        
        // Get old sessions
        var oldSessions = await _database!.Table<ChatSession>()
            .Where(s => s.LastModifiedAt < cutoffDate)
            .ToListAsync();
        
        int count = 0;
        foreach (var session in oldSessions)
        {
            await DeleteSessionAsync(session.Id);
            count++;
        }
        
        Debug.WriteLine($"[ChatDatabaseService] Purged {count} sessions older than {retentionDays} days");
        return count;
    }

    #endregion

    #region Messages

    /// <summary>
    /// Get all messages for a session.
    /// </summary>
    public async Task<List<ChatMessage>> GetMessagesAsync(int sessionId)
    {
        await InitAsync();
        return await _database!.Table<ChatMessage>()
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.Timestamp)
            .ToListAsync();
    }

    /// <summary>
    /// Save a message to the database.
    /// </summary>
    public async Task SaveMessageAsync(ChatMessage message)
    {
        await InitAsync();
        
        var existing = await _database!.Table<ChatMessage>()
            .FirstOrDefaultAsync(m => m.Id == message.Id);
        
        if (existing != null)
        {
            await _database!.UpdateAsync(message);
        }
        else
        {
            await _database!.InsertAsync(message);
        }
        
        // Update session message count and last modified
        var session = await GetSessionAsync(message.SessionId);
        if (session != null)
        {
            var messageCount = await _database!.Table<ChatMessage>()
                .CountAsync(m => m.SessionId == message.SessionId);
            session.MessageCount = messageCount;
            await UpdateSessionAsync(session);
        }
    }

    /// <summary>
    /// Update session title based on first user message.
    /// </summary>
    public async Task UpdateSessionTitleFromFirstMessageAsync(int sessionId)
    {
        await InitAsync();
        var firstUserMessage = await _database!.Table<ChatMessage>()
            .Where(m => m.SessionId == sessionId && m.RoleValue == (int)ChatRole.User)
            .OrderBy(m => m.Timestamp)
            .FirstOrDefaultAsync();
        
        if (firstUserMessage != null)
        {
            var session = await GetSessionAsync(sessionId);
            if (session != null && session.Title == "New Chat")
            {
                // Take first 50 chars of first user message as title
                var title = firstUserMessage.ContentValue;
                if (title.Length > 50)
                    title = title[..47] + "...";
                session.Title = title;
                await UpdateSessionAsync(session);
            }
        }
    }

    #endregion
}
