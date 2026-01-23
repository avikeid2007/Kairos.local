using KaiROS.AI.Models;

using System.IO;
using System.Text;

namespace KaiROS.AI.Services;

public interface IDocumentService
{
    List<Models.Document> LoadedDocuments { get; }
    Task<Models.Document> LoadDocumentAsync(string filePath);
    Task<string> GetDocumentContentAsync(string filePath);
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
            // Extract text content using shared method
            document.Content = await GetDocumentContentAsync(filePath);

            System.Diagnostics.Debug.WriteLine($"[RAG] Content extracted: {document.Content?.Length ?? 0} characters");

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
            System.Diagnostics.Debug.WriteLine($"[RAG] ERROR loading file: {ex.Message}");
            document.Content = $"Error reading file: {ex.Message}";
            document.Chunks = new List<DocumentChunk>();
        }

        _documents.Add(document);
        System.Diagnostics.Debug.WriteLine($"[RAG] Document added. Total documents: {_documents.Count}");
        return document;
    }

    public async Task<string> GetDocumentContentAsync(string filePath)
    {
        if (!File.Exists(filePath)) return string.Empty;

        var extension = Path.GetExtension(filePath).ToLower();
        var type = GetDocumentType(extension);

        try
        {
            if (type == DocumentType.Word)
            {
                System.Diagnostics.Debug.WriteLine("[RAG] Reading as Word document...");
                return await ReadWordDocumentAsync(filePath);
            }
            else if (type == DocumentType.Pdf)
            {
                System.Diagnostics.Debug.WriteLine("[RAG] Reading as PDF document...");
                var content = await ReadPdfDocumentAsync(filePath);

                // OCR Fallback: If text extraction yields too little content, try OCR
                if (string.IsNullOrWhiteSpace(content) || content.Trim().Length < 50)
                {
                    System.Diagnostics.Debug.WriteLine("[RAG] Text extraction result empty or minimal. Attempting OCR...");
                    try
                    {
                        var ocrContent = await ReadPdfWithOcrAsync(filePath);
                        if (!string.IsNullOrWhiteSpace(ocrContent))
                        {
                            System.Diagnostics.Debug.WriteLine($"[RAG] OCR successful. Extracted {ocrContent.Length} chars.");
                            return ocrContent;
                        }
                    }
                    catch (Exception ocrEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[RAG] OCR failed: {ocrEx.Message}");
                    }
                }

                return content;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[RAG] Reading as text file...");
                return await ReadTextFileAsync(filePath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RAG] Error extracting text: {ex.Message}");
            throw;
        }
    }

    private static async Task<string> ReadPdfWithOcrAsync(string filePath)
    {
        if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            return string.Empty;

        try
        {
            var sb = new StringBuilder();

            // 1. Load PDF using Windows.Data.Pdf
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(filePath);
            var pdfDocument = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);

            // 2. Initialize OCR Engine
            var ocrEngine = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
            if (ocrEngine == null)
            {
                // Fallback to English if user profile language not supported
                ocrEngine = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));
            }

            if (ocrEngine == null)
            {
                System.Diagnostics.Debug.WriteLine("[RAG-OCR] Failed to create OCR Engine.");
                return string.Empty;
            }

            System.Diagnostics.Debug.WriteLine($"[RAG-OCR] Processing {pdfDocument.PageCount} pages with OCR...");

            // 3. Process each page
            for (uint i = 0; i < pdfDocument.PageCount; i++)
            {
                using var page = pdfDocument.GetPage(i);

                // Render page to stream
                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(stream);

                // Create SoftwareBitmap from stream
                var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
                using var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

                // Run OCR
                var result = await ocrEngine.RecognizeAsync(softwareBitmap);

                if (result.Lines.Count > 0)
                {
                    foreach (var line in result.Lines)
                    {
                        sb.AppendLine(line.Text);
                    }
                    sb.AppendLine(); // Spacing between pages
                }
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RAG-OCR] Error: {ex.Message}");
            return string.Empty;
        }
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
