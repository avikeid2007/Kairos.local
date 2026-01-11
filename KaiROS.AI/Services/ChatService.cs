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
    private LLamaContext? _context;
    private InteractiveExecutor? _executor;
    private InferenceStats _lastStats = new();
    private uint _currentContextSize;

    public bool IsModelLoaded => _modelManager.ActiveModel != null && _context != null;
    public InferenceStats LastStats => _lastStats;
    public uint CurrentContextSize => _currentContextSize;

    public event EventHandler<string>? TokenGenerated;
    public event EventHandler<InferenceStats>? StatsUpdated;

    public ChatService(ModelManagerService modelManager, IDocumentService documentService)
    {
        _modelManager = modelManager;
        _documentService = documentService;
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
    }

    private void DisposeContext()
    {
        _executor = null;
        _context?.Dispose();
        _context = null;
    }

    public async Task<string> GenerateResponseAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        var response = new StringBuilder();
        await foreach (var token in GenerateResponseStreamAsync(messages, cancellationToken))
        {
            response.Append(token);
        }
        return response.ToString();
    }

    public async IAsyncEnumerable<string> GenerateResponseStreamAsync(
        IEnumerable<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_executor == null || _context == null)
        {
            yield return "Error: No model loaded. Please select and load a model first.";
            yield break;
        }

        var prompt = BuildPrompt(messages, _documentService);
        var inferenceParams = new InferenceParams
        {
            MaxTokens = 256,
            AntiPrompts = new[] { "User:", "\nUser:", "###", "Human:", "\nHuman:", "### User", "### Human" }
        };

        // Strings to filter out from output
        var unwantedStrings = new[] {
            "###", "User:", "Human:", "Assistant:", "### ", "\n### ",
            "## OUTPUT:", "##OUTPUT:", "## OUTPUT", "##OUTPUT",
            "**OUTPUT:**", "**OUTPUT**", "OUTPUT:",
            "## Response:", "##Response:", "<|assistant|>", "<|end|>"
        };

        var stopwatch = Stopwatch.StartNew();
        int tokenCount = 0;
        var startMemory = GC.GetTotalMemory(false);
        var buffer = new StringBuilder();

        await foreach (var token in _executor.InferAsync(prompt, inferenceParams, cancellationToken))
        {
            tokenCount++;

            // Filter out unwanted strings
            var cleanToken = token;
            foreach (var unwanted in unwantedStrings)
            {
                cleanToken = cleanToken.Replace(unwanted, "");
            }

            // Only yield non-empty tokens
            if (!string.IsNullOrEmpty(cleanToken))
            {
                TokenGenerated?.Invoke(this, cleanToken);
                yield return cleanToken;
            }

            // Update stats periodically
            if (tokenCount % 10 == 0)
            {
                UpdateStats(stopwatch.Elapsed, tokenCount, startMemory);
            }
        }

        stopwatch.Stop();
        UpdateStats(stopwatch.Elapsed, tokenCount, startMemory);
    }

    private void UpdateStats(TimeSpan elapsed, int tokenCount, long startMemory)
    {
        _lastStats = new InferenceStats
        {
            GeneratedTokens = tokenCount,
            TotalTokens = tokenCount,
            ElapsedTime = elapsed,
            TokensPerSecond = elapsed.TotalSeconds > 0 ? tokenCount / elapsed.TotalSeconds : 0,
            MemoryUsageBytes = GC.GetTotalMemory(false) - startMemory,
            BackendInUse = "CPU",
            ContextSize = _currentContextSize,
            GpuLayers = _modelManager.CurrentGpuLayers
        };

        StatsUpdated?.Invoke(this, _lastStats);
    }

    private static string BuildPrompt(IEnumerable<ChatMessage> messages, IDocumentService documentService)
    {
        var sb = new StringBuilder();
        var messageList = messages.ToList();

        // Get user's latest message to find relevant context
        var latestUserMessage = messageList.LastOrDefault(m => m.Role == ChatRole.User);
        string documentContext = string.Empty;

        System.Diagnostics.Debug.WriteLine($"[RAG] BuildPrompt called. Has user message: {latestUserMessage != null}, Loaded docs: {documentService.LoadedDocuments.Count}");

        if (latestUserMessage != null && documentService.LoadedDocuments.Count > 0)
        {
            documentContext = documentService.GetContextForQuery(latestUserMessage.Content, 3);
            System.Diagnostics.Debug.WriteLine($"[RAG] Context retrieved: {documentContext?.Length ?? 0} characters");
            if (!string.IsNullOrEmpty(documentContext))
            {
                System.Diagnostics.Debug.WriteLine($"[RAG] Context preview: {documentContext.Substring(0, Math.Min(200, documentContext.Length))}...");
            }
        }

        foreach (var msg in messageList)
        {
            switch (msg.Role)
            {
                case ChatRole.System:
                    var systemContent = msg.Content;
                    if (!string.IsNullOrEmpty(documentContext))
                    {
                        systemContent += "\n\nContext:\n" + documentContext;
                    }
                    sb.AppendLine($"### System:\n{systemContent}\n");
                    break;
                case ChatRole.User:
                    // Only include the last user message to prevent fake conversation generation
                    break;
                case ChatRole.Assistant:
                    // Skip previous assistant messages
                    break;
            }
        }

        // Add only the last user message
        var lastUser = messageList.LastOrDefault(m => m.Role == ChatRole.User);
        if (lastUser != null)
        {
            sb.AppendLine($"### User:\n{lastUser.Content}\n");
        }

        // If there's document context but no system message, add default system
        if (!messageList.Any(m => m.Role == ChatRole.System))
        {
            var sysPrompt = "You are a helpful assistant. Be concise and direct.";
            if (!string.IsNullOrEmpty(documentContext))
            {
                sysPrompt += "\n\nContext:\n" + documentContext;
            }
            sb.Insert(0, $"### System:\n{sysPrompt}\n\n");
        }

        sb.AppendLine("### Assistant:");
        return sb.ToString();
    }

    public void ClearContext()
    {
        if (_context != null)
        {
            DisposeContext();
            InitializeContext();
        }
    }
}
