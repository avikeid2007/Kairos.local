namespace KaiROS.Mobile.Models;

/// <summary>
/// Represents a document loaded for RAG context.
/// </summary>
public class DocumentContext
{
    public string FileName { get; set; } = string.Empty;
    
    public string FilePath { get; set; } = string.Empty;
    
    public string Content { get; set; } = string.Empty;
    
    public string FileType { get; set; } = string.Empty;
    
    public long FileSizeBytes { get; set; }
    
    public DateTime LoadedAt { get; set; } = DateTime.Now;
    
    /// <summary>
    /// Truncated preview of content for display.
    /// </summary>
    public string Preview => Content.Length > 200 
        ? Content[..200] + "..." 
        : Content;
    
    /// <summary>
    /// Formatted file size for display.
    /// </summary>
    public string FormattedSize => FileSizeBytes switch
    {
        < 1024 => $"{FileSizeBytes} B",
        < 1024 * 1024 => $"{FileSizeBytes / 1024.0:F1} KB",
        _ => $"{FileSizeBytes / (1024.0 * 1024.0):F1} MB"
    };
}
