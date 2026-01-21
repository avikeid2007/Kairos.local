using SQLite;

namespace KaiROS.Mobile.Models;

/// <summary>
/// Represents a system prompt preset/persona.
/// </summary>
[Table("PromptPresets")]
public class SystemPromptPreset
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    
    public string Name { get; set; } = "New Preset";
    
    public string PromptText { get; set; } = string.Empty;
    
    public string Icon { get; set; } = "🤖";
    
    public bool IsDefault { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
