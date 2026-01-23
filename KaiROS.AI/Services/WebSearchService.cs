using HtmlAgilityPack;
using System.Net.Http;
using System.Web;
using System.Text.RegularExpressions;

namespace KaiROS.AI.Services;

public class WebSearchService : IWebSearchService
{
    private readonly HttpClient _httpClient;

    public WebSearchService()
    {
        _httpClient = new HttpClient();
        // Mimic a real browser to avoid instant blocking
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
    }

    public async Task<List<SearchResult>> SearchAsync(string query, int maxResults = 5, CancellationToken cancellationToken = default)
    {
        var results = new List<SearchResult>();
        try
        {
            // DuckDuckGo HTML search URL
            var url = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(query)}";

            var response = await _httpClient.GetStringAsync(url, cancellationToken);

            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(response);

            // Select result nodes
            // Note: DDG HTML structure can change, but usually results are in div with class 'result'
            var resultNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'result')]");

            if (resultNodes != null)
            {
                foreach (var node in resultNodes.Take(maxResults))
                {
                    var titleNode = node.SelectSingleNode(".//a[@class='result__a']");
                    var snippetNode = node.SelectSingleNode(".//a[@class='result__snippet']");

                    if (titleNode != null && snippetNode != null)
                    {
                        var link = titleNode.GetAttributeValue("href", "");

                        // DDG redirect URLs usually look like /l/?kh=-1&uddg=https%3A%2F%2Fexample.com
                        if (link.Contains("uddg="))
                        {
                            var match = Regex.Match(link, "uddg=([^&]+)");
                            if (match.Success)
                            {
                                link = HttpUtility.UrlDecode(match.Groups[1].Value);
                            }
                        }

                        results.Add(new SearchResult
                        {
                            Title = HttpUtility.HtmlDecode(titleNode.InnerText),
                            Link = link,
                            Snippet = HttpUtility.HtmlDecode(snippetNode.InnerText)
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebSearch] Error: {ex.Message}");
            // Return empty list or whatever results we managed to get
        }

        return results;
    }

    public async Task<string> GetPageContentAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetStringAsync(url, cancellationToken);
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(response);

            // Remove script, style, and other non-content tags
            doc.DocumentNode.Descendants()
                .Where(n => n.Name == "script" || n.Name == "style" || n.Name == "nav" || n.Name == "footer" || n.Name == "header" || n.Name == "aside" || n.Name == "iframe" || n.Name == "noscript")
                .ToList()
                .ForEach(n => n.Remove());

            // Inject newlines after block elements to preserve structure
            var blockTags = new[] { "p", "div", "br", "li", "h1", "h2", "h3", "h4", "h5", "h6", "section", "article" };
            foreach (var tag in blockTags)
            {
                var nodes = doc.DocumentNode.SelectNodes($"//{tag}");
                if (nodes != null)
                {
                    foreach (var node in nodes)
                    {
                        node.ParentNode.InsertAfter(doc.CreateTextNode("\n"), node);
                    }
                }
            }

            // Extract text from the body
            var body = doc.DocumentNode.SelectSingleNode("//body");
            if (body == null) return string.Empty;

            var text = HttpUtility.HtmlDecode(body.InnerText);

            // Normalize line endings
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");

            // Collapse multiple spaces/tabs to single space (but preserve newlines)
            text = Regex.Replace(text, @"[ \t]+", " ");

            // Collapse multiple newlines
            text = Regex.Replace(text, @"\n{3,}", "\n\n");

            // Trim
            text = text.Trim();

            // Sanitize antiprompt triggers
            text = text.Replace("###", "")
                       .Replace("User:", "User -")
                       .Replace("Assistant:", "Assistant -")
                       .Replace("Human:", "Human -");

            // Truncate to avoid context limit issues (reduced to 1500)
            if (text.Length > 1500)
            {
                text = text.Substring(0, 1500) + "...";
            }

            return text;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebSearch] Error fetching content for {url}: {ex.Message}");
            return string.Empty;
        }
    }
}
