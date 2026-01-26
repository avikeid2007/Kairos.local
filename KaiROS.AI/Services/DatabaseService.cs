using System.IO;
using Microsoft.Data.Sqlite;
using KaiROS.AI.Models;

namespace KaiROS.AI.Services;

public interface IDatabaseService
{
    Task InitializeAsync();
    // Models
    Task<List<CustomModelEntity>> GetCustomModelsAsync();
    Task AddCustomModelAsync(CustomModelEntity model);
    Task DeleteCustomModelAsync(int id);
    
    // RaaS
    Task<List<RaasConfiguration>> GetRaasConfigsAsync();
    Task AddRaasConfigAsync(RaasConfiguration config);
    Task DeleteRaasConfigAsync(string id);
    Task AddRagSourceAsync(string configId, RagSource source);
    Task DeleteRagSourceAsync(string sourceId);
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
        try
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
                );

                CREATE TABLE IF NOT EXISTS RaasConfigurations (
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Port INTEGER NOT NULL,
                    SystemPrompt TEXT,
                    Description TEXT
                );

                CREATE TABLE IF NOT EXISTS RagSources (
                    Id TEXT PRIMARY KEY,
                    ConfigId TEXT NOT NULL,
                    Type INTEGER NOT NULL,
                    Name TEXT NOT NULL,
                    Value TEXT NOT NULL,
                    IsEnabled INTEGER DEFAULT 1,
                    FOREIGN KEY(ConfigId) REFERENCES RaasConfigurations(Id) ON DELETE CASCADE
                );";

            await command.ExecuteNonQueryAsync();
        }
        catch (SqliteException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[KaiROS] Database initialization error: {ex.Message}");
        }
    }

    // ... (CustomModels methods omitted, keeping them as is) ...

    public async Task<List<CustomModelEntity>> GetCustomModelsAsync()
    {
        var models = new List<CustomModelEntity>();

        try
        {
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
        }
        catch (SqliteException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[KaiROS] Database read error: {ex.Message}");
        }

        return models;
    }

    public async Task AddCustomModelAsync(CustomModelEntity model)
    {
        try
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
        catch (SqliteException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[KaiROS] Database write error: {ex.Message}");
            throw new InvalidOperationException($"Failed to save model: {ex.Message}", ex);
        }
    }

    public async Task DeleteCustomModelAsync(int id)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM CustomModels WHERE Id = $id";
            command.Parameters.AddWithValue("$id", id);

            await command.ExecuteNonQueryAsync();
        }
        catch (SqliteException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[KaiROS] Database delete error: {ex.Message}");
        }
    }

    // --- RaaS Methods ---

    public async Task<List<RaasConfiguration>> GetRaasConfigsAsync()
    {
        var configs = new List<RaasConfiguration>();
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // 1. Get Configs
            var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM RaasConfigurations";
            
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    configs.Add(new RaasConfiguration
                    {
                        Id = reader.GetString(0),
                        Name = reader.GetString(1),
                        Port = reader.GetInt32(2),
                        SystemPrompt = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                        Description = reader.IsDBNull(4) ? string.Empty : reader.GetString(4)
                    });
                }
            }

            // 2. Get Sources
            foreach (var config in configs)
            {
                var sourceCmd = connection.CreateCommand();
                sourceCmd.CommandText = "SELECT * FROM RagSources WHERE ConfigId = $configId";
                sourceCmd.Parameters.AddWithValue("$configId", config.Id);

                await using var sourceReader = await sourceCmd.ExecuteReaderAsync();
                while (await sourceReader.ReadAsync())
                {
                    config.Sources.Add(new RagSource
                    {
                        Id = sourceReader.GetString(0),
                        // ConfigId is index 1
                        Type = (RagSourceType)sourceReader.GetInt32(2),
                        Name = sourceReader.GetString(3),
                        Value = sourceReader.GetString(4),
                        IsEnabled = sourceReader.GetInt32(5) == 1
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[KaiROS] RaaS load error: {ex.Message}");
        }
        return configs;
    }

    public async Task AddRaasConfigAsync(RaasConfiguration config)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO RaasConfigurations (Id, Name, Port, SystemPrompt, Description)
                VALUES ($id, $name, $port, $prompt, $desc)";
            
            command.Parameters.AddWithValue("$id", config.Id);
            command.Parameters.AddWithValue("$name", config.Name);
            command.Parameters.AddWithValue("$port", config.Port);
            command.Parameters.AddWithValue("$prompt", config.SystemPrompt ?? string.Empty);
            command.Parameters.AddWithValue("$desc", config.Description ?? string.Empty);

            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[KaiROS] RaaS save error: {ex.Message}");
            throw;
        }
    }

    public async Task DeleteRaasConfigAsync(string id)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            // Delete config (Cascade should handle sources, but manual delete is safer for File Cleanup logic in Service)
            // Ideally, we delete sources first to get their paths for cleanup? 
            // The Service layer handles file cleanup before calling this.
            
            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM RaasConfigurations WHERE Id = $id";
            command.Parameters.AddWithValue("$id", id);
            
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
             System.Diagnostics.Debug.WriteLine($"[KaiROS] RaaS delete error: {ex.Message}");
        }
    }

    public async Task AddRagSourceAsync(string configId, RagSource source)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO RagSources (Id, ConfigId, Type, Name, Value, IsEnabled)
                VALUES ($id, $configId, $type, $name, $value, $enabled)";

            command.Parameters.AddWithValue("$id", source.Id);
            command.Parameters.AddWithValue("$configId", configId);
            command.Parameters.AddWithValue("$type", (int)source.Type);
            command.Parameters.AddWithValue("$name", source.Name);
            command.Parameters.AddWithValue("$value", source.Value);
            command.Parameters.AddWithValue("$enabled", source.IsEnabled ? 1 : 0);

            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
             System.Diagnostics.Debug.WriteLine($"[KaiROS] Source add error: {ex.Message}");
             throw;
        }
    }

    public async Task DeleteRagSourceAsync(string sourceId)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM RagSources WHERE Id = $id";
            command.Parameters.AddWithValue("$id", sourceId);

            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
             System.Diagnostics.Debug.WriteLine($"[KaiROS] Source delete error: {ex.Message}");
        }
    }
}
