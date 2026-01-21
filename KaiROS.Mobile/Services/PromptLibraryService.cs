using KaiROS.Mobile.Models;
using SQLite;
using System.Diagnostics;

namespace KaiROS.Mobile.Services;

/// <summary>
/// Service for managing prompt presets/personas.
/// </summary>
public class PromptLibraryService
{
    private SQLiteAsyncConnection? _database;
    private readonly string _databasePath;
    private SystemPromptPreset? _activePreset;

    public event EventHandler<SystemPromptPreset?>? ActivePresetChanged;

    public SystemPromptPreset? ActivePreset => _activePreset;

    public PromptLibraryService()
    {
        _databasePath = Path.Combine(FileSystem.AppDataDirectory, "chathistory.db");
    }

    private async Task InitAsync()
    {
        if (_database != null)
            return;

        _database = new SQLiteAsyncConnection(_databasePath);
        await _database.CreateTableAsync<SystemPromptPreset>();
        
        // Seed default presets if empty
        var count = await _database.Table<SystemPromptPreset>().CountAsync();
        if (count == 0)
        {
            await SeedDefaultPresetsAsync();
        }
        
        // Load active preset
        await LoadActivePresetAsync();
    }

    private async Task SeedDefaultPresetsAsync()
    {
        var defaults = new List<SystemPromptPreset>
        {
            new()
            {
                Name = "Helpful Assistant",
                PromptText = "You are a helpful AI assistant. Be concise and accurate in your responses.",
                Icon = "🤖",
                IsDefault = true
            },
            new()
            {
                Name = "Creative Writer",
                PromptText = "You are a creative writing assistant. Help with stories, poems, and creative content. Be imaginative and expressive.",
                Icon = "✍️"
            },
            new()
            {
                Name = "Code Helper",
                PromptText = "You are a programming assistant. Help with code, debugging, and technical explanations. Be precise and provide examples.",
                Icon = "💻"
            },
            new()
            {
                Name = "Summarizer",
                PromptText = "You are a summarization expert. Provide clear, concise summaries of information. Focus on key points.",
                Icon = "📋"
            },
            new()
            {
                Name = "Translator",
                PromptText = "You are a language translator. Translate text accurately while preserving meaning and tone.",
                Icon = "🌐"
            }
        };

        foreach (var preset in defaults)
        {
            await _database!.InsertAsync(preset);
        }
    }

    public async Task<List<SystemPromptPreset>> GetAllPresetsAsync()
    {
        await InitAsync();
        return await _database!.Table<SystemPromptPreset>()
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<SystemPromptPreset?> GetPresetAsync(int id)
    {
        await InitAsync();
        return await _database!.Table<SystemPromptPreset>()
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task SavePresetAsync(SystemPromptPreset preset)
    {
        await InitAsync();
        
        if (preset.Id == 0)
        {
            await _database!.InsertAsync(preset);
        }
        else
        {
            await _database!.UpdateAsync(preset);
        }
    }

    public async Task DeletePresetAsync(int id)
    {
        await InitAsync();
        await _database!.Table<SystemPromptPreset>()
            .DeleteAsync(p => p.Id == id);
        
        // If deleted preset was active, switch to default
        if (_activePreset?.Id == id)
        {
            var defaultPreset = await _database!.Table<SystemPromptPreset>()
                .FirstOrDefaultAsync(p => p.IsDefault);
            await SetActivePresetAsync(defaultPreset?.Id ?? 0);
        }
    }

    public async Task SetActivePresetAsync(int presetId)
    {
        await InitAsync();
        
        _activePreset = await GetPresetAsync(presetId);
        Preferences.Set("ActivePresetId", presetId);
        
        Debug.WriteLine($"[PromptLibraryService] Active preset: {_activePreset?.Name}");
        ActivePresetChanged?.Invoke(this, _activePreset);
    }

    private async Task LoadActivePresetAsync()
    {
        var activeId = Preferences.Get("ActivePresetId", 0);
        
        if (activeId > 0)
        {
            _activePreset = await GetPresetAsync(activeId);
        }
        
        // Fallback to default if not found
        if (_activePreset == null)
        {
            _activePreset = await _database!.Table<SystemPromptPreset>()
                .FirstOrDefaultAsync(p => p.IsDefault);
            
            if (_activePreset != null)
            {
                Preferences.Set("ActivePresetId", _activePreset.Id);
            }
        }
    }

    public string GetActivePromptText()
    {
        return _activePreset?.PromptText 
            ?? "You are a helpful AI assistant. Be concise and accurate in your responses.";
    }
}
