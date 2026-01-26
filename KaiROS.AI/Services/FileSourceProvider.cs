using KaiROS.AI.Models;
using System.IO;
using System.Text;

namespace KaiROS.AI.Services;

public class FileSourceProvider : IRagSourceProvider
{
    public RagSourceType SupportedType => RagSourceType.File;

    public async Task<string> GetContentAsync(RagSource source)
    {
        if (source.Type != RagSourceType.File)
            throw new ArgumentException("Invalid source type for FileSourceProvider");

        string filePath = source.Value;
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        // Use existing logic or simplified logic here. 
        // Ideally we reuse the robust logic from DocumentService, 
        // but for now we'll put the reading logic here to separate concerns over time.
        // Or better yet, we can move the static readers from DocumentService to here or a helper.
        
        var extension = Path.GetExtension(filePath).ToLower();
        return extension switch
        {
            ".pdf" => await ReadPdfDocumentAsync(filePath),
            ".docx" => await ReadWordDocumentAsync(filePath),
            ".doc" => await ReadWordDocumentAsync(filePath),
            _ => await File.ReadAllTextAsync(filePath)
        };
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
                        if (!string.IsNullOrWhiteSpace(text)) sb.AppendLine(text);
                    }
                }
                return sb.ToString();
            }
            catch (Exception ex) { return $"Error reading Word doc: {ex.Message}"; }
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
                        sb.AppendLine();
                    }
                }
                return sb.ToString();
            }
            catch (Exception ex) { return $"Error reading PDF: {ex.Message}"; }
        });
    }
}
