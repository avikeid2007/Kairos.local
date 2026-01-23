using KaiROS.AI.Models;
using LLama;
using LLama.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace KaiROS.AI.Services;

public class ChatService : IChatService
{
    private readonly ModelManagerService _modelManager;
    private readonly IDocumentService _documentService;
    private readonly IWebSearchService _webSearchService;
    private LLamaContext? _context;
    private InteractiveExecutor? _executor;
    private InferenceStats _lastStats = new();
    private uint _currentContextSize;
    private bool _isSystemPromptSent = false;

    public bool IsModelLoaded => _modelManager.ActiveModel != null && _context != null;
    public InferenceStats LastStats => _lastStats;
    public uint CurrentContextSize => _currentContextSize;

    public event EventHandler<string>? TokenGenerated;
    public event EventHandler<InferenceStats>? StatsUpdated;

    public ChatService(ModelManagerService modelManager, IDocumentService documentService, IWebSearchService webSearchService)
    {
        _modelManager = modelManager;
        _documentService = documentService;
        _webSearchService = webSearchService;
        _modelManager.ModelLoaded += OnModelLoaded;
        _modelManager.ModelUnloaded += OnModelUnloaded;
    }

    private void OnModelLoaded(object? sender, LLMModelInfo model)
    {
        InitializeContext();
    }

    private void OnModelUnloaded(object? sender, EventArgs e)
    {
        DisposeContext();
    }

    private void InitializeContext()
    {
        var weights = _modelManager.GetLoadedWeights();
        if (weights == null) return;

        _currentContextSize = 8192; // Default context size
        _context = weights.CreateContext(new ModelParams(_modelManager.ActiveModel?.LocalPath ?? "")
        {
            ContextSize = _currentContextSize
        });

        _executor = new InteractiveExecutor(_context);
        _isSystemPromptSent = false;
    }

    private void DisposeContext()
    {
        _executor = null;
        _context?.Dispose();
        _context = null;
        _isSystemPromptSent = false;
    }

    public void ClearContext()
    {
        if (_context != null)
        {
            DisposeContext();
            InitializeContext();
        }
    }

    // Interface Implementations
    public Task<string> GenerateResponseAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default)
        => GenerateResponseAsync(messages, false, cancellationToken);

    public async Task<string> GenerateResponseAsync(IEnumerable<ChatMessage> messages, bool useWebSearch, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        await foreach (var token in GenerateResponseStreamAsync(messages, useWebSearch, null, false, cancellationToken))
        {
            sb.Append(token);
        }
        return sb.ToString();
    }

    public IAsyncEnumerable<string> GenerateResponseStreamAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default)
        => GenerateResponseStreamAsync(messages, false, null, false, cancellationToken);

    public IAsyncEnumerable<string> GenerateResponseStreamAsync(IEnumerable<ChatMessage> messages, bool useWebSearch, CancellationToken cancellationToken = default)
        => GenerateResponseStreamAsync(messages, useWebSearch, null, false, cancellationToken);

    public async IAsyncEnumerable<string> GenerateResponseStreamAsync(
        IEnumerable<ChatMessage> messages,
        bool useWebSearch,
        string? sessionContext,
        bool useGlobalRag,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_executor == null || _context == null)
        {
            yield return "Error: No model loaded. Please select and load a model first.";
            yield break;
        }

        string webContext = "";

        // Handle Web Search
        if (useWebSearch)
        {
            var lastUserMessage = messages.LastOrDefault(m => m.Role == ChatRole.User);
            if (lastUserMessage != null)
            {
                yield return "[Searching web...]";
                var searchResult = await PerformWebSearchAsync(lastUserMessage.Content, cancellationToken);
                yield return searchResult.Status;
                webContext = searchResult.Context;
            }
        }

        string prompt;
        if (!_isSystemPromptSent)
        {
            // First turn: Include System Prompt + Context + First User Message
            prompt = BuildFullPrompt(messages, _documentService, webContext, sessionContext, useGlobalRag);
            _isSystemPromptSent = true;
        }
        else
        {
            // Follow-up turn: Only new User Message
            prompt = BuildFollowUpPrompt(messages, webContext);
        }

        var inferenceParams = new InferenceParams
        {
            MaxTokens = 4096,
            AntiPrompts = new[] { "User:", "\nUser:", "###", "Human:", "\nHuman:", "### User", "### Human" }
        };

        var unwantedStrings = new[] {
            "###", "User:", "Human:", "Assistant:", "### ", "\n### ",
            "## OUTPUT:", "##OUTPUT:", "## OUTPUT", "##OUTPUT",
            "**OUTPUT:**", "**OUTPUT**", "OUTPUT:",
            "## Response:", "##Response:", "<|assistant|>", "<|end|>"
        };

        var stopwatch = Stopwatch.StartNew();
        int tokenCount = 0;
        var startMemory = GC.GetTotalMemory(false);

        await foreach (var token in _executor.InferAsync(prompt, inferenceParams, cancellationToken))
        {
            tokenCount++;
            var cleanToken = token;
            foreach (var unwanted in unwantedStrings) cleanToken = cleanToken.Replace(unwanted, "");

            if (!string.IsNullOrEmpty(cleanToken))
            {
                TokenGenerated?.Invoke(this, cleanToken);
                yield return cleanToken;
            }

            if (tokenCount % 10 == 0) UpdateStats(stopwatch.Elapsed, tokenCount, startMemory);
        }

        stopwatch.Stop();
        UpdateStats(stopwatch.Elapsed, tokenCount, startMemory);
    }

    private async Task<(string Context, string Status)> PerformWebSearchAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            var results = await _webSearchService.SearchAsync(query, 3, cancellationToken);
            if (results.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Web Search Results:");
                var topResults = results.Take(2).ToList();
                foreach (var result in topResults)
                {
                    var content = await _webSearchService.GetPageContentAsync(result.Link, cancellationToken);
                    sb.AppendLine($"--- Source: {result.Title} ({result.Link}) ---");
                    if (!string.IsNullOrEmpty(content)) sb.AppendLine(content);
                    else sb.AppendLine($"Snippet: {result.Snippet}");
                    sb.AppendLine("--- End Source ---\n");
                }
                return (sb.ToString(), "\r[Found info] ");
            }
            else
            {
                return ("", "\r[No results] ");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Web search failed: {ex}");
            return ("", "\r[Search failed] ");
        }
    }

    // New helper method for stats
    private void UpdateStats(TimeSpan elapsed, int tokenCount, long startMemory)
    {
        var usedMemory = GC.GetTotalMemory(false) - startMemory;
        _lastStats = new InferenceStats
        {
            ElapsedTime = elapsed,
            TokensPerSecond = tokenCount / (elapsed.TotalSeconds > 0 ? elapsed.TotalSeconds : 1),
            GeneratedTokens = tokenCount,
            MemoryUsageBytes = usedMemory > 0 ? usedMemory : 0
        };
        StatsUpdated?.Invoke(this, _lastStats);
    }

    private static string BuildFullPrompt(IEnumerable<ChatMessage> messages, IDocumentService documentService, string webContext = "", string? sessionContext = null, bool useGlobalRag = false)
    {
        var sb = new StringBuilder();
        var messageList = messages.ToList();

        var latestUserMessage = messageList.LastOrDefault(m => m.Role == ChatRole.User);
        string documentContext = string.Empty;

        // Priority 1: Session Specific Context
        if (!string.IsNullOrEmpty(sessionContext))
            documentContext += "Attached Document Content:\n" + sessionContext + "\n\n";

        // Priority 2: Global RAG
        if (useGlobalRag && latestUserMessage != null && documentService.LoadedDocuments.Count > 0)
        {
            var globalContext = documentService.GetContextForQuery(latestUserMessage.Content, 3);
            if (!string.IsNullOrEmpty(globalContext))
                documentContext += "Global Knowledge Base:\n" + globalContext + "\n\n";
        }

        string combinedContext = "";
        if (!string.IsNullOrEmpty(documentContext)) combinedContext += "Context:\n" + documentContext + "\n\n";
        if (!string.IsNullOrEmpty(webContext)) combinedContext += webContext + "\n\n";

        var systemMsg = messageList.FirstOrDefault(m => m.Role == ChatRole.System);
        var systemContent = systemMsg?.Content ?? "You are a helpful assistant. Be concise and direct.";

        if (!string.IsNullOrEmpty(combinedContext))
            systemContent += "\n\n" + combinedContext;

        sb.AppendLine($"### System:\n{systemContent}\n");

        if (latestUserMessage != null)
        {
            sb.AppendLine($"### User:\n{latestUserMessage.Content}\n");
        }

        sb.AppendLine("### Assistant:");
        return sb.ToString();
    }

    private static string BuildFollowUpPrompt(IEnumerable<ChatMessage> messages, string webContext = "")
    {
        var sb = new StringBuilder();
        var latestUserMessage = messages.LastOrDefault(m => m.Role == ChatRole.User);

        if (latestUserMessage != null)
        {
            if (!string.IsNullOrEmpty(webContext))
            {
                sb.AppendLine($"### System:\n[Additional Information]\n{webContext}\n");
            }

            sb.AppendLine($"### User:\n{latestUserMessage.Content}\n");
        }

        sb.AppendLine("### Assistant:");
        return sb.ToString();
    }
}
