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
    private LLamaContext? _context;
    private InteractiveExecutor? _executor;
    private InferenceStats _lastStats = new();
    
    public bool IsModelLoaded => _modelManager.ActiveModel != null && _context != null;
    public InferenceStats LastStats => _lastStats;
    
    public event EventHandler<string>? TokenGenerated;
    public event EventHandler<InferenceStats>? StatsUpdated;
    
    public ChatService(ModelManagerService modelManager)
    {
        _modelManager = modelManager;
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
        
        _context = weights.CreateContext(new ModelParams(_modelManager.ActiveModel?.LocalPath ?? "")
        {
            ContextSize = 4096
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
        
        var prompt = BuildPrompt(messages);
        var inferenceParams = new InferenceParams
        {
            MaxTokens = 2048,
            AntiPrompts = new[] { "User:", "\nUser:", "###", "Human:", "\nHuman:", "### User", "### Human" }
        };
        
        // Strings to filter out from output
        var unwantedStrings = new[] { "###", "User:", "Human:", "Assistant:", "### ", "\n### " };
        
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
            BackendInUse = "CPU" // Will be updated based on actual backend
        };
        
        StatsUpdated?.Invoke(this, _lastStats);
    }
    
    private static string BuildPrompt(IEnumerable<ChatMessage> messages)
    {
        var sb = new StringBuilder();
        
        foreach (var msg in messages)
        {
            switch (msg.Role)
            {
                case ChatRole.System:
                    sb.AppendLine($"### System:\n{msg.Content}\n");
                    break;
                case ChatRole.User:
                    sb.AppendLine($"### User:\n{msg.Content}\n");
                    break;
                case ChatRole.Assistant:
                    sb.AppendLine($"### Assistant:\n{msg.Content}\n");
                    break;
            }
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
