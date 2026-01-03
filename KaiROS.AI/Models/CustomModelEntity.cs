namespace KaiROS.AI.Models;

/// <summary>
/// Entity representing a user-added custom model stored in SQLite
/// </summary>
public class CustomModelEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime AddedDate { get; set; } = DateTime.UtcNow;
    public bool IsLocal { get; set; } // true = local file, false = download URL
}
