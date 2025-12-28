using KaiROS.AI.Models;
using Microsoft.Data.Sqlite;
using System.IO;

namespace KaiROS.AI.Services;

public interface ISessionService
{
    Task InitializeAsync();
    Task<List<ChatSession>> GetAllSessionsAsync();
    Task<ChatSession> CreateSessionAsync(string? modelName = null, string? systemPrompt = null);
    Task<ChatSession?> GetSessionAsync(int sessionId);
    Task UpdateSessionAsync(ChatSession session);
    Task DeleteSessionAsync(int sessionId);
    Task AddMessageAsync(int sessionId, ChatMessage message);
    Task<List<ChatMessage>> GetMessagesAsync(int sessionId);
    Task ClearMessagesAsync(int sessionId);
}

public class SessionService : ISessionService
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private bool _initialized;
    
    public SessionService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KaiROS.AI");
        Directory.CreateDirectory(appDataPath);
        
        _dbPath = Path.Combine(appDataPath, "sessions.db");
        _connectionString = $"Data Source={_dbPath}";
        
        // Initialize database synchronously in constructor
        InitializeDatabase();
    }
    
    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        
        // Create sessions table
        var createSessionsTable = @"
            CREATE TABLE IF NOT EXISTS Sessions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                ModelName TEXT,
                SystemPrompt TEXT,
                MessageCount INTEGER DEFAULT 0
            )";
        
        using var cmd1 = new SqliteCommand(createSessionsTable, connection);
        cmd1.ExecuteNonQuery();
        
        // Create messages table
        var createMessagesTable = @"
            CREATE TABLE IF NOT EXISTS Messages (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId INTEGER NOT NULL,
                Role TEXT NOT NULL,
                Content TEXT NOT NULL,
                Timestamp TEXT NOT NULL,
                FOREIGN KEY (SessionId) REFERENCES Sessions(Id) ON DELETE CASCADE
            )";
        
        using var cmd2 = new SqliteCommand(createMessagesTable, connection);
        cmd2.ExecuteNonQuery();
        
        _initialized = true;
    }
    
    public async Task InitializeAsync()
    {
        // Database is already initialized in constructor
        // This method is kept for interface compatibility
        if (!_initialized)
        {
            InitializeDatabase();
        }
        await Task.CompletedTask;
    }
    
    public async Task<List<ChatSession>> GetAllSessionsAsync()
    {
        var sessions = new List<ChatSession>();
        
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        var query = "SELECT * FROM Sessions ORDER BY UpdatedAt DESC";
        await using var cmd = new SqliteCommand(query, connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            sessions.Add(ReadSession(reader));
        }
        
        return sessions;
    }
    
    public async Task<ChatSession> CreateSessionAsync(string? modelName = null, string? systemPrompt = null)
    {
        var session = new ChatSession
        {
            Title = "New Chat",
            ModelName = modelName,
            SystemPrompt = systemPrompt,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        var query = @"
            INSERT INTO Sessions (Title, CreatedAt, UpdatedAt, ModelName, SystemPrompt, MessageCount)
            VALUES (@Title, @CreatedAt, @UpdatedAt, @ModelName, @SystemPrompt, 0);
            SELECT last_insert_rowid();";
        
        await using var cmd = new SqliteCommand(query, connection);
        cmd.Parameters.AddWithValue("@Title", session.Title);
        cmd.Parameters.AddWithValue("@CreatedAt", session.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@UpdatedAt", session.UpdatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@ModelName", (object?)modelName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SystemPrompt", (object?)systemPrompt ?? DBNull.Value);
        
        var id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        session.Id = id;
        
        return session;
    }
    
    public async Task<ChatSession?> GetSessionAsync(int sessionId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        var query = "SELECT * FROM Sessions WHERE Id = @Id";
        await using var cmd = new SqliteCommand(query, connection);
        cmd.Parameters.AddWithValue("@Id", sessionId);
        
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var session = ReadSession(reader);
            session.Messages = await GetMessagesAsync(sessionId);
            return session;
        }
        
        return null;
    }
    
    public async Task UpdateSessionAsync(ChatSession session)
    {
        session.UpdatedAt = DateTime.Now;
        
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        var query = @"
            UPDATE Sessions 
            SET Title = @Title, UpdatedAt = @UpdatedAt, ModelName = @ModelName, 
                SystemPrompt = @SystemPrompt, MessageCount = @MessageCount
            WHERE Id = @Id";
        
        await using var cmd = new SqliteCommand(query, connection);
        cmd.Parameters.AddWithValue("@Id", session.Id);
        cmd.Parameters.AddWithValue("@Title", session.Title);
        cmd.Parameters.AddWithValue("@UpdatedAt", session.UpdatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@ModelName", (object?)session.ModelName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SystemPrompt", (object?)session.SystemPrompt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@MessageCount", session.MessageCount);
        
        await cmd.ExecuteNonQueryAsync();
    }
    
    public async Task DeleteSessionAsync(int sessionId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        // Delete messages first
        var deleteMessages = "DELETE FROM Messages WHERE SessionId = @SessionId";
        await using var cmd1 = new SqliteCommand(deleteMessages, connection);
        cmd1.Parameters.AddWithValue("@SessionId", sessionId);
        await cmd1.ExecuteNonQueryAsync();
        
        // Delete session
        var deleteSession = "DELETE FROM Sessions WHERE Id = @Id";
        await using var cmd2 = new SqliteCommand(deleteSession, connection);
        cmd2.Parameters.AddWithValue("@Id", sessionId);
        await cmd2.ExecuteNonQueryAsync();
    }
    
    public async Task AddMessageAsync(int sessionId, ChatMessage message)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        var query = @"
            INSERT INTO Messages (SessionId, Role, Content, Timestamp)
            VALUES (@SessionId, @Role, @Content, @Timestamp)";
        
        await using var cmd = new SqliteCommand(query, connection);
        cmd.Parameters.AddWithValue("@SessionId", sessionId);
        cmd.Parameters.AddWithValue("@Role", message.Role.ToString());
        cmd.Parameters.AddWithValue("@Content", message.Content);
        cmd.Parameters.AddWithValue("@Timestamp", message.Timestamp.ToString("o"));
        
        await cmd.ExecuteNonQueryAsync();
        
        // Update message count and timestamp
        var updateSession = @"
            UPDATE Sessions 
            SET MessageCount = (SELECT COUNT(*) FROM Messages WHERE SessionId = @SessionId),
                UpdatedAt = @UpdatedAt
            WHERE Id = @SessionId";
        
        await using var cmd2 = new SqliteCommand(updateSession, connection);
        cmd2.Parameters.AddWithValue("@SessionId", sessionId);
        cmd2.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("o"));
        await cmd2.ExecuteNonQueryAsync();
    }
    
    public async Task<List<ChatMessage>> GetMessagesAsync(int sessionId)
    {
        var messages = new List<ChatMessage>();
        
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        var query = "SELECT * FROM Messages WHERE SessionId = @SessionId ORDER BY Timestamp";
        await using var cmd = new SqliteCommand(query, connection);
        cmd.Parameters.AddWithValue("@SessionId", sessionId);
        
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var role = Enum.Parse<ChatRole>(reader.GetString(2));
            var content = reader.GetString(3);
            var timestamp = DateTime.Parse(reader.GetString(4));
            
            messages.Add(new ChatMessage
            {
                Role = role,
                Content = content,
                Timestamp = timestamp
            });
        }
        
        return messages;
    }
    
    public async Task ClearMessagesAsync(int sessionId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        var query = "DELETE FROM Messages WHERE SessionId = @SessionId";
        await using var cmd = new SqliteCommand(query, connection);
        cmd.Parameters.AddWithValue("@SessionId", sessionId);
        await cmd.ExecuteNonQueryAsync();
        
        // Update message count
        var updateSession = "UPDATE Sessions SET MessageCount = 0, UpdatedAt = @UpdatedAt WHERE Id = @SessionId";
        await using var cmd2 = new SqliteCommand(updateSession, connection);
        cmd2.Parameters.AddWithValue("@SessionId", sessionId);
        cmd2.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("o"));
        await cmd2.ExecuteNonQueryAsync();
    }
    
    private static ChatSession ReadSession(SqliteDataReader reader)
    {
        return new ChatSession
        {
            Id = reader.GetInt32(0),
            Title = reader.GetString(1),
            CreatedAt = DateTime.Parse(reader.GetString(2)),
            UpdatedAt = DateTime.Parse(reader.GetString(3)),
            ModelName = reader.IsDBNull(4) ? null : reader.GetString(4),
            SystemPrompt = reader.IsDBNull(5) ? null : reader.GetString(5),
            MessageCount = reader.GetInt32(6)
        };
    }
}
