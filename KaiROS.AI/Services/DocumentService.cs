using KaiROS.AI.Models;

using System.IO;
using System.Text;

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
    private const int ChunkSize = 1500; // Characters per chunk - increased for better context
    private const int ChunkOverlap = 100; // Overlap between chunks for continuity
    private const int SmallDocumentThreshold = 8000; // Documents smaller than this get full context

    public List<Models.Document> LoadedDocuments => _documents.ToList();

    public async Task<Models.Document> LoadDocumentAsync(string filePath)

    {
        System.Diagnostics.Debug.WriteLine($"[RAG] Loading document: {filePath}");

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

        System.Diagnostics.Debug.WriteLine($"[RAG] Document type: {document.Type}, Size: {fileInfo.Length} bytes");

        try
        {
            // Extract text content based on file type
            if (document.Type == DocumentType.Word)
            {
                System.Diagnostics.Debug.WriteLine("[RAG] Reading as Word document...");
                document.Content = await ReadWordDocumentAsync(filePath);
            }
            else if (document.Type == DocumentType.Pdf)
            {
                System.Diagnostics.Debug.WriteLine("[RAG] Reading as PDF document...");
                document.Content = await ReadPdfDocumentAsync(filePath);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[RAG] Reading as text file...");
                document.Content = await ReadTextFileAsync(filePath);
            }

            System.Diagnostics.Debug.WriteLine($"[RAG] Content extracted: {document.Content?.Length ?? 0} characters");
            System.Diagnostics.Debug.WriteLine($"[RAG] First 200 chars: {document.Content?.Substring(0, Math.Min(200, document.Content?.Length ?? 0))}");

            // Create chunks for RAG (with safety)
            if (!string.IsNullOrEmpty(document.Content))
            {
                document.Chunks = CreateChunksSimple(document.Content);
                System.Diagnostics.Debug.WriteLine($"[RAG] Created {document.Chunks.Count} chunks");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[RAG] WARNING: Content is null or empty!");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RAG] ERROR reading file: {ex.Message}");
            document.Content = $"Error reading file: {ex.Message}";
            document.Chunks = new List<DocumentChunk>();
        }

        _documents.Add(document);
        System.Diagnostics.Debug.WriteLine($"[RAG] Document added. Total documents: {_documents.Count}");
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
    /// Uses improved keyword matching with numerical/date awareness
    /// </summary>
    public string GetContextForQuery(string query, int maxChunks = 5)
    {
        System.Diagnostics.Debug.WriteLine($"[RAG] GetContextForQuery called. Documents: {_documents.Count}, Query: {query?.Substring(0, Math.Min(50, query?.Length ?? 0))}...");

        if (_documents.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine("[RAG] No documents loaded, returning empty context");
            return string.Empty;
        }

        // Check if any document is small enough for full context mode
        var totalContentLength = _documents.Sum(d => d.Content?.Length ?? 0);
        if (totalContentLength <= SmallDocumentThreshold)
        {
            System.Diagnostics.Debug.WriteLine($"[RAG] Small document mode: returning full content ({totalContentLength} chars)");
            return GetFullDocumentContext();
        }

        // Log total chunks available
        var totalChunks = _documents.Sum(d => d.Chunks.Count);
        System.Diagnostics.Debug.WriteLine($"[RAG] Total chunks available: {totalChunks}");

        // Enhanced query word extraction - keep shorter words for numbers and dates
        var queryWords = (query ?? string.Empty).ToLower()
            .Split(new[] { ' ', '.', ',', '?', '!' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 || IsNumberOrDate(w)) // Keep numbers and short important words
            .ToHashSet();

        // Add query-related keywords for common document terms
        AddRelatedKeywords(queryWords, query ?? string.Empty);

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

        System.Diagnostics.Debug.WriteLine($"[RAG] Scored chunks: {scoredChunks.Count}, Top chunks: {topChunks.Count}");

        if (topChunks.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine("[RAG] No matching chunks, using fallback (multiple chunks per doc)");
            // Return multiple chunks from each document as fallback
            var sb = new StringBuilder();
            foreach (var doc in _documents.Take(2))
            {
                var chunksToTake = Math.Min(3, doc.Chunks.Count);
                for (int i = 0; i < chunksToTake; i++)
                {
                    sb.AppendLine($"[From {doc.FileName} - Part {i + 1}]:");
                    sb.AppendLine(doc.Chunks[i].Content);
                    sb.AppendLine();
                }
            }
            System.Diagnostics.Debug.WriteLine($"[RAG] Fallback context length: {sb.Length} chars");
            return sb.ToString();
        }

        // Build context string
        var context = new StringBuilder();
        context.AppendLine("--- DOCUMENT CONTEXT ---");

        foreach (var (chunk, doc, score) in topChunks)
        {
            System.Diagnostics.Debug.WriteLine($"[RAG] Including chunk from {doc.FileName} with score {score}");
            context.AppendLine($"[From {doc.FileName}]:");
            context.AppendLine(chunk.Content);
            context.AppendLine();
        }

        context.AppendLine("--- END CONTEXT ---");
        System.Diagnostics.Debug.WriteLine($"[RAG] Context built: {context.Length} characters");
        return context.ToString();
    }

    /// <summary>
    /// Returns full document content for small documents
    /// </summary>
    private string GetFullDocumentContext()
    {
        var sb = new StringBuilder();
        sb.AppendLine("--- FULL DOCUMENT CONTEXT ---");

        foreach (var doc in _documents)
        {
            sb.AppendLine($"[Document: {doc.FileName}]:");
            sb.AppendLine(doc.Content);
            sb.AppendLine();
        }

        sb.AppendLine("--- END CONTEXT ---");
        return sb.ToString();
    }

    /// <summary>
    /// Check if a word is a number, date, or date-related term
    /// </summary>
    private static bool IsNumberOrDate(string word)
    {
        // Check for numbers (including amounts like 50000, 1234.56)
        if (double.TryParse(word.Replace(",", ""), out _))
            return true;

        // Check for common date/quarter patterns
        var datePatterns = new[] { "q1", "q2", "q3", "q4", "jan", "feb", "mar", "apr", "may", "jun",
            "jul", "aug", "sep", "oct", "nov", "dec", "2024", "2025", "2026" };
        return datePatterns.Contains(word.ToLower());
    }

    /// <summary>
    /// Add related keywords based on query context for better matching
    /// </summary>
    private static void AddRelatedKeywords(HashSet<string> queryWords, string query)
    {
        var lowerQuery = query.ToLower();

        // Tax-related keyword expansions
        if (lowerQuery.Contains("tax") || lowerQuery.Contains("tds"))
        {
            queryWords.UnionWith(new[] { "tax", "tds", "deducted", "deduction", "amount", "challan", "deposited" });
        }

        // Quarterly-related expansions
        if (lowerQuery.Contains("quarter") || lowerQuery.Contains("quarterly"))
        {
            queryWords.UnionWith(new[] { "q1", "q2", "q3", "q4", "quarter", "quarterly", "april", "june", "july", "september", "october", "december", "january", "march" });
        }

        // Salary-related expansions
        if (lowerQuery.Contains("salary") || lowerQuery.Contains("income"))
        {
            queryWords.UnionWith(new[] { "salary", "income", "gross", "net", "allowance", "exemption", "section" });
        }

        // Deduction-related expansions
        if (lowerQuery.Contains("deduction") || lowerQuery.Contains("section"))
        {
            queryWords.UnionWith(new[] { "deduction", "section", "16", "10", "chapter", "vi-a", "80c", "80d" });
        }
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
