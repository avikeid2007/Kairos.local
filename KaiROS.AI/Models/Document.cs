namespace KaiROS.AI.Models;

/// <summary>
/// Represents a loaded document for RAG context
/// </summary>
public class Document
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DocumentType Type { get; set; }
    public string Content { get; set; } = string.Empty;
    public List<DocumentChunk> Chunks { get; set; } = new();
    public DateTime LoadedAt { get; set; } = DateTime.Now;
    public long FileSizeBytes { get; set; }
    
    public string FileSizeText => FileSizeBytes switch
    {
        < 1024 => $"{FileSizeBytes} B",
        < 1024 * 1024 => $"{FileSizeBytes / 1024.0:F1} KB",
        _ => $"{FileSizeBytes / (1024.0 * 1024.0):F1} MB"
    };
}

/// <summary>
/// A chunk of a document used for context retrieval
/// </summary>
public class DocumentChunk
{
    public int Index { get; set; }
    public string Content { get; set; } = string.Empty;
    public int StartPosition { get; set; }
    public int EndPosition { get; set; }
}

public enum DocumentType
{
    Text,
    Pdf,
    Word,
    Unknown
}
