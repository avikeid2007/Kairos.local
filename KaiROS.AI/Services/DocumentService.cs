using System.IO;
using System.Text;
using KaiROS.AI.Models;

namespace KaiROS.AI.Services;

public interface IDocumentService
{
    List<Models.Document> LoadedDocuments { get; }
    Task<Models.Document> LoadDocumentAsync(string filePath);
    void RemoveDocument(string documentId);
    void ClearAllDocuments();
    string GetContextForQuery(string query, int maxChunks = 3);
}

public class DocumentService : IDocumentService
{
    private readonly List<Models.Document> _documents = new();
    private const int ChunkSize = 500; // Characters per chunk
    private const int ChunkOverlap = 50; // Overlap between chunks
    
    public List<Models.Document> LoadedDocuments => _documents.ToList();
    
    public async Task<Models.Document> LoadDocumentAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Document not found", filePath);
        
        var fileInfo = new FileInfo(filePath);
        var extension = fileInfo.Extension.ToLower();
        
        var document = new Models.Document
        {
            FileName = fileInfo.Name,
            FilePath = filePath,
            FileSizeBytes = fileInfo.Length,
            Type = GetDocumentType(extension)
        };
        
        try
        {
            // Extract text content based on file type
            if (document.Type == DocumentType.Word)
            {
                document.Content = await ReadWordDocumentAsync(filePath);
            }
            else if (document.Type == DocumentType.Pdf)
            {
                document.Content = await ReadPdfDocumentAsync(filePath);
            }
            else
            {
                document.Content = await ReadTextFileAsync(filePath);
            }
            
            // Create chunks for RAG (with safety)
            if (!string.IsNullOrEmpty(document.Content))
            {
                document.Chunks = CreateChunksSimple(document.Content);
            }
        }
        catch (Exception ex)
        {
            document.Content = $"Error reading file: {ex.Message}";
            document.Chunks = new List<DocumentChunk>();
        }
        
        _documents.Add(document);
        return document;
    }
    
    public void RemoveDocument(string documentId)
    {
        var doc = _documents.FirstOrDefault(d => d.Id == documentId);
        if (doc != null)
        {
            _documents.Remove(doc);
        }
    }
    
    public void ClearAllDocuments()
    {
        _documents.Clear();
    }
    
    /// <summary>
    /// Get relevant context from loaded documents for a query
    /// Uses simple keyword matching for now
    /// </summary>
    public string GetContextForQuery(string query, int maxChunks = 3)
    {
        if (_documents.Count == 0)
            return string.Empty;
        
        var queryWords = query.ToLower()
            .Split(new[] { ' ', '.', ',', '?', '!' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3) // Filter short words
            .ToHashSet();
        
        // Score all chunks
        var scoredChunks = new List<(DocumentChunk chunk, Document doc, int score)>();
        
        foreach (var doc in _documents)
        {
            foreach (var chunk in doc.Chunks)
            {
                var chunkWords = chunk.Content.ToLower()
                    .Split(new[] { ' ', '.', ',', '?', '!' }, StringSplitOptions.RemoveEmptyEntries)
                    .ToHashSet();
                
                var score = queryWords.Intersect(chunkWords).Count();
                if (score > 0)
                {
                    scoredChunks.Add((chunk, doc, score));
                }
            }
        }
        
        // Get top chunks
        var topChunks = scoredChunks
            .OrderByDescending(x => x.score)
            .Take(maxChunks)
            .ToList();
        
        if (topChunks.Count == 0)
        {
            // Return first chunk of each document as fallback
            var sb = new StringBuilder();
            foreach (var doc in _documents.Take(2))
            {
                if (doc.Chunks.Count > 0)
                {
                    sb.AppendLine($"[From {doc.FileName}]:");
                    sb.AppendLine(doc.Chunks[0].Content);
                    sb.AppendLine();
                }
            }
            return sb.ToString();
        }
        
        // Build context string
        var context = new StringBuilder();
        context.AppendLine("--- DOCUMENT CONTEXT ---");
        
        foreach (var (chunk, doc, _) in topChunks)
        {
            context.AppendLine($"[From {doc.FileName}]:");
            context.AppendLine(chunk.Content);
            context.AppendLine();
        }
        
        context.AppendLine("--- END CONTEXT ---");
        return context.ToString();
    }
    
    private static DocumentType GetDocumentType(string extension)
    {
        return extension switch
        {
            ".txt" or ".md" or ".csv" or ".json" or ".xml" => DocumentType.Text,
            ".docx" => DocumentType.Word,
            ".doc" => DocumentType.Word,
            ".pdf" => DocumentType.Pdf,
            _ => DocumentType.Unknown
        };
    }
    
    private static async Task<string> ReadTextFileAsync(string filePath)
    {
        return await File.ReadAllTextAsync(filePath);
    }
    
    private static async Task<string> ReadWordDocumentAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                var sb = new StringBuilder();
                
                using var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(filePath, false);
                var body = doc.MainDocumentPart?.Document?.Body;
                
                if (body != null)
                {
                    foreach (var element in body.ChildElements)
                    {
                        var text = element.InnerText;
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            sb.AppendLine(text);
                        }
                    }
                }
                
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"Error reading Word document: {ex.Message}";
            }
        });
    }
    
    private static async Task<string> ReadPdfDocumentAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                var sb = new StringBuilder();
                
                using var pdfReader = new iText.Kernel.Pdf.PdfReader(filePath);
                using var pdfDocument = new iText.Kernel.Pdf.PdfDocument(pdfReader);
                
                for (int i = 1; i <= pdfDocument.GetNumberOfPages(); i++)
                {
                    var page = pdfDocument.GetPage(i);
                    var text = iText.Kernel.Pdf.Canvas.Parser.PdfTextExtractor.GetTextFromPage(page);
                    
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        sb.AppendLine(text);
                        sb.AppendLine(); // Add spacing between pages
                    }
                }
                
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"Error reading PDF document: {ex.Message}";
            }
        });
    }
    
    /// <summary>
    /// Simple chunking that just splits on paragraphs - very safe approach
    /// </summary>
    private static List<DocumentChunk> CreateChunksSimple(string content)
    {
        var chunks = new List<DocumentChunk>();
        
        if (string.IsNullOrEmpty(content))
            return chunks;
        
        // Simple approach: split into paragraphs, then group into chunks
        var paragraphs = content.Split(new[] { "\r\n\r\n", "\n\n", "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        
        var currentChunk = new StringBuilder();
        var index = 0;
        var startPos = 0;
        
        foreach (var para in paragraphs)
        {
            var trimmed = para.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            
            if (currentChunk.Length + trimmed.Length > ChunkSize && currentChunk.Length > 0)
            {
                // Save current chunk
                chunks.Add(new DocumentChunk
                {
                    Index = index++,
                    Content = currentChunk.ToString().Trim(),
                    StartPosition = startPos,
                    EndPosition = startPos + currentChunk.Length
                });
                
                startPos += currentChunk.Length;
                currentChunk.Clear();
            }
            
            currentChunk.AppendLine(trimmed);
        }
        
        // Add remaining content
        if (currentChunk.Length > 0)
        {
            chunks.Add(new DocumentChunk
            {
                Index = index,
                Content = currentChunk.ToString().Trim(),
                StartPosition = startPos,
                EndPosition = startPos + currentChunk.Length
            });
        }
        
        return chunks;
    }
    
    private static List<DocumentChunk> CreateChunks(string content)
    {
        var chunks = new List<DocumentChunk>();
        
        if (string.IsNullOrEmpty(content))
            return chunks;
        
        var index = 0;
        var position = 0;
        var maxIterations = content.Length / 10 + 100; // Safety limit
        var iterations = 0;
        
        while (position < content.Length && iterations < maxIterations)
        {
            iterations++;
            var endPosition = Math.Min(position + ChunkSize, content.Length);
            
            // Try to break at sentence or word boundary
            if (endPosition < content.Length)
            {
                var searchLength = Math.Min(100, endPosition - position);
                if (searchLength > 0)
                {
                    var lastSentence = content.LastIndexOf('.', endPosition - 1, searchLength);
                    if (lastSentence > position)
                    {
                        endPosition = lastSentence + 1;
                    }
                    else
                    {
                        var spaceSearchLength = Math.Min(50, endPosition - position);
                        if (spaceSearchLength > 0)
                        {
                            var lastSpace = content.LastIndexOf(' ', endPosition - 1, spaceSearchLength);
                            if (lastSpace > position)
                            {
                                endPosition = lastSpace;
                            }
                        }
                    }
                }
            }
            
            // Ensure we take at least one character
            if (endPosition <= position)
            {
                endPosition = Math.Min(position + ChunkSize, content.Length);
            }
            
            var chunkContent = content.Substring(position, endPosition - position).Trim();
            
            if (!string.IsNullOrEmpty(chunkContent))
            {
                chunks.Add(new DocumentChunk
                {
                    Index = index++,
                    Content = chunkContent,
                    StartPosition = position,
                    EndPosition = endPosition
                });
            }
            
            // Always advance position - use overlap only if it still advances
            var newPosition = endPosition - ChunkOverlap;
            position = Math.Max(newPosition, position + 1);
            
            if (position >= content.Length) break;
        }
        
        return chunks;
    }
}
