using KaiROS.AI.Models;
using System.Text;
using System.IO;

namespace KaiROS.AI.Services;

public class RagEngine
{
    private readonly IEnumerable<IRagSourceProvider> _providers;
    private readonly List<Models.Document> _documents = new();
    
    private const int ChunkSize = 1500;
    private const int ChunkOverlap = 100;
    private const int SmallDocumentThreshold = 8000;

    public RagEngine(IEnumerable<IRagSourceProvider> providers)
    {
        _providers = providers;
    }

    public async Task AddSourceAsync(RagSource source)
    {
        var provider = _providers.FirstOrDefault(p => p.SupportedType == source.Type);
        if (provider == null)
        {
            // Fallback for text/other? 
            throw new NotSupportedException($"No provider found for source type {source.Type}");
        }

        try
        {
            var content = await provider.GetContentAsync(source);
            
            var doc = new Models.Document
            {
                Id = source.Id, 
                FileName = source.Name,
                FilePath = source.Value,
                Content = content,
                Type = GetDocumentTypeFromSource(source)
            };

            if (!string.IsNullOrEmpty(content))
            {
                doc.Chunks = CreateChunksSimple(content);
            }

            _documents.Add(doc);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error adding source {source.Name}: {ex.Message}");
            throw;
        }
    }

    public string GetContext(string query, int maxChunks = 5)
    {
        if (_documents.Count == 0) return string.Empty;

        // Same logic as DocumentService logic
        var totalContentLength = _documents.Sum(d => d.Content?.Length ?? 0);
        if (totalContentLength <= SmallDocumentThreshold)
        {
            return GetFullContext();
        }

        // Search logic
        var queryWords = (query ?? string.Empty).ToLower()
            .Split(new[] { ' ', '.', ',', '?', '!' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2)
            .ToHashSet();

        var scoredChunks = new List<(DocumentChunk chunk, Models.Document doc, int score)>();

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

        var topChunks = scoredChunks
            .OrderByDescending(x => x.score)
            .Take(maxChunks)
            .ToList();

        if (topChunks.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("--- CONTEXT ---");
        foreach (var (chunk, doc, score) in topChunks)
        {
            sb.AppendLine($"[Source: {doc.FileName}]:");
            sb.AppendLine(chunk.Content);
            sb.AppendLine();
        }
        sb.AppendLine("--- END CONTEXT ---");
        return sb.ToString();
    }
    
    // ... Helper methods (GetFullContext, CreateChunksSimple, etc.) ...
    
    private string GetFullContext()
    {
         var sb = new StringBuilder();
        sb.AppendLine("--- FULL CONTEXT ---");
        foreach (var doc in _documents)
        {
            sb.AppendLine($"[Source: {doc.FileName}]:");
            sb.AppendLine(doc.Content);
            sb.AppendLine();
        }
        sb.AppendLine("--- END CONTEXT ---");
        return sb.ToString();
    }

    private static List<DocumentChunk> CreateChunksSimple(string content)
    {
        var chunks = new List<DocumentChunk>();
        if (string.IsNullOrEmpty(content)) return chunks;

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

    private static DocumentType GetDocumentTypeFromSource(RagSource source)
    {
        if (source.Type == RagSourceType.Web) return DocumentType.Unknown;
        
        var ext = Path.GetExtension(source.Value)?.ToLower() ?? "";
        
        if (ext == ".pdf") return DocumentType.Pdf;
        if (ext == ".docx") return DocumentType.Word;
        
        return DocumentType.Text;
    }
}
