using KaiROS.AI.Models;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace KaiROS.AI.Services;

public class WebSourceProvider : IRagSourceProvider
{
    public RagSourceType SupportedType => RagSourceType.Web;

    private readonly HttpClient _httpClient;

    public WebSourceProvider()
    {
        _httpClient = new HttpClient();
        // Fake user agent to avoid basic blocks
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
    }

    public async Task<string> GetContentAsync(RagSource source)
    {
        if (string.IsNullOrWhiteSpace(source.Value)) return string.Empty;

        try
        {
            var html = await _httpClient.GetStringAsync(source.Value);
            return ConvertHtmlToText(html);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error fetching web source {source.Value}: {ex.Message}");
            return $"[Error reading content from {source.Value}: {ex.Message}]";
        }
    }

    private string ConvertHtmlToText(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;

        // 1. Remove script and style tags
        html = Regex.Replace(html, "<script.*?</script>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        html = Regex.Replace(html, "<style.*?</style>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // 2. Remove HTML tags
        var plainText = Regex.Replace(html, "<.*?>", " ");

        // 3. Decode HTML entities
        plainText = System.Net.WebUtility.HtmlDecode(plainText);

        // 4. Normalize whitespace
        plainText = Regex.Replace(plainText, @"\s+", " ").Trim();

        return plainText;
    }
}
