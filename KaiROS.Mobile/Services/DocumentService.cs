using KaiROS.Mobile.Models;
using System.Diagnostics;
using System.Text;

namespace KaiROS.Mobile.Services;

/// <summary>
/// Service for loading and parsing documents for RAG context.
/// Supports text, markdown, and basic document formats.
/// </summary>
public class DocumentService
{
    // Maximum file size for loading (2MB)
    private const long MAX_FILE_SIZE = 2 * 1024 * 1024;
    
    // Maximum content length for context (to avoid overloading the model)
    private const int MAX_CONTENT_LENGTH = 8000;
    
    private readonly List<DocumentContext> _loadedDocuments = new();
    
    public IReadOnlyList<DocumentContext> LoadedDocuments => _loadedDocuments.AsReadOnly();
    
    public event EventHandler? DocumentsChanged;

    /// <summary>
    /// Pick and load a document using file picker.
    /// </summary>
    public async Task<DocumentContext?> PickAndLoadDocumentAsync()
    {
        try
        {
            var fileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.Android, new[] { "text/*", "application/pdf" } },
                { DevicePlatform.iOS, new[] { "public.text", "public.plain-text" } },
                { DevicePlatform.WinUI, new[] { ".txt", ".md", ".json", ".csv", ".xml" } }
            });

            var options = new PickOptions
            {
                PickerTitle = "Select a document for context",
                FileTypes = fileTypes
            };

            var result = await FilePicker.Default.PickAsync(options);
            if (result == null)
                return null;

            return await LoadDocumentAsync(result.FullPath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DocumentService] Pick error: {ex}");
            return null;
        }
    }

    /// <summary>
    /// Load a document from file path.
    /// </summary>
    public async Task<DocumentContext?> LoadDocumentAsync(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            
            if (!fileInfo.Exists)
            {
                Debug.WriteLine($"[DocumentService] File not found: {filePath}");
                return null;
            }

            if (fileInfo.Length > MAX_FILE_SIZE)
            {
                Debug.WriteLine($"[DocumentService] File too large: {fileInfo.Length} bytes");
                await Shell.Current.DisplayAlert("File Too Large", 
                    $"File size exceeds 2MB limit. Please use a smaller file.", "OK");
                return null;
            }

            var content = await File.ReadAllTextAsync(filePath);
            
            // Truncate if too long
            if (content.Length > MAX_CONTENT_LENGTH)
            {
                content = content[..MAX_CONTENT_LENGTH] + "\n\n[Content truncated due to length...]";
            }

            var document = new DocumentContext
            {
                FileName = fileInfo.Name,
                FilePath = filePath,
                Content = content,
                FileType = fileInfo.Extension.ToLowerInvariant(),
                FileSizeBytes = fileInfo.Length,
                LoadedAt = DateTime.Now
            };

            _loadedDocuments.Add(document);
            DocumentsChanged?.Invoke(this, EventArgs.Empty);
            
            Debug.WriteLine($"[DocumentService] Loaded: {document.FileName} ({document.FormattedSize})");
            return document;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DocumentService] Load error: {ex}");
            await Shell.Current.DisplayAlert("Load Error", 
                $"Could not load file: {ex.Message}", "OK");
            return null;
        }
    }

    /// <summary>
    /// Remove a loaded document.
    /// </summary>
    public void RemoveDocument(DocumentContext document)
    {
        _loadedDocuments.Remove(document);
        DocumentsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Clear all loaded documents.
    /// </summary>
    public void ClearAllDocuments()
    {
        _loadedDocuments.Clear();
        DocumentsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Build a context string from all loaded documents for injection into prompts.
    /// </summary>
    public string BuildContextString()
    {
        if (_loadedDocuments.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("### Document Context:");
        sb.AppendLine("The following documents have been provided as reference material:\n");

        foreach (var doc in _loadedDocuments)
        {
            sb.AppendLine($"--- {doc.FileName} ---");
            sb.AppendLine(doc.Content);
            sb.AppendLine();
        }

        sb.AppendLine("--- End of Documents ---\n");
        sb.AppendLine("Please use the above documents to help answer the user's questions.\n");

        return sb.ToString();
    }

    /// <summary>
    /// Check if any documents are loaded.
    /// </summary>
    public bool HasDocuments => _loadedDocuments.Count > 0;
}
