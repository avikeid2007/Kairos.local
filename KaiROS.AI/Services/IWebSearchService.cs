using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KaiROS.AI.Services
{
    public interface IWebSearchService
    {
        Task<List<SearchResult>> SearchAsync(string query, int maxResults = 5, CancellationToken cancellationToken = default);
        Task<string> GetPageContentAsync(string url, CancellationToken cancellationToken = default);
    }

    public class SearchResult
    {
        public string Title { get; set; } = string.Empty;
        public string Link { get; set; } = string.Empty;
        public string Snippet { get; set; } = string.Empty;
    }
}
