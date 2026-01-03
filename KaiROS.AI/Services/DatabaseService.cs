using System.IO;
using Microsoft.Data.Sqlite;
using KaiROS.AI.Models;

namespace KaiROS.AI.Services;

public interface IDatabaseService
{
    Task InitializeAsync();
    Task<List<CustomModelEntity>> GetCustomModelsAsync();
    Task AddCustomModelAsync(CustomModelEntity model);
    Task DeleteCustomModelAsync(int id);
}

public class DatabaseService : IDatabaseService
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    
    public DatabaseService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KaiROS.AI");
        
        Directory.CreateDirectory(appDataPath);
        _dbPath = Path.Combine(appDataPath, "kairos.db");
        _connectionString = $"Data Source={_dbPath}";
    }
    
    public async Task InitializeAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS CustomModels (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                DisplayName TEXT NOT NULL,
                Description TEXT,
                FilePath TEXT,
                DownloadUrl TEXT,
                SizeBytes INTEGER DEFAULT 0,
                AddedDate TEXT NOT NULL,
                IsLocal INTEGER DEFAULT 0
            )";
        
        await command.ExecuteNonQueryAsync();
    }
    
    public async Task<List<CustomModelEntity>> GetCustomModelsAsync()
    {
        var models = new List<CustomModelEntity>();
        
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM CustomModels ORDER BY AddedDate DESC";
        
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            models.Add(new CustomModelEntity
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                DisplayName = reader.GetString(2),
                Description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                FilePath = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                DownloadUrl = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                SizeBytes = reader.IsDBNull(6) ? 0 : reader.GetInt64(6),
                AddedDate = DateTime.Parse(reader.GetString(7)),
                IsLocal = reader.GetInt32(8) == 1
            });
        }
        
        return models;
    }
    
    public async Task AddCustomModelAsync(CustomModelEntity model)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO CustomModels (Name, DisplayName, Description, FilePath, DownloadUrl, SizeBytes, AddedDate, IsLocal)
            VALUES ($name, $displayName, $description, $filePath, $downloadUrl, $sizeBytes, $addedDate, $isLocal)";
        
        command.Parameters.AddWithValue("$name", model.Name);
        command.Parameters.AddWithValue("$displayName", model.DisplayName);
        command.Parameters.AddWithValue("$description", model.Description ?? string.Empty);
        command.Parameters.AddWithValue("$filePath", model.FilePath ?? string.Empty);
        command.Parameters.AddWithValue("$downloadUrl", model.DownloadUrl ?? string.Empty);
        command.Parameters.AddWithValue("$sizeBytes", model.SizeBytes);
        command.Parameters.AddWithValue("$addedDate", model.AddedDate.ToString("O"));
        command.Parameters.AddWithValue("$isLocal", model.IsLocal ? 1 : 0);
        
        await command.ExecuteNonQueryAsync();
    }
    
    public async Task DeleteCustomModelAsync(int id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM CustomModels WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        
        await command.ExecuteNonQueryAsync();
    }
}
