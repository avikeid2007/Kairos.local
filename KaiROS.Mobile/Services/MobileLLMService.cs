using KaiROS.Mobile.Models;
using LLama;
using LLama.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace KaiROS.Mobile.Services;

/// <summary>
/// Mobile-optimized LLM service for Android devices.
/// Uses reduced context size and memory-efficient settings.
/// </summary>
public class MobileLLMService : IDisposable
{
    // Mobile-optimized: smaller context than desktop (512 vs 8192)
    private const uint MOBILE_CONTEXT_SIZE = 512;
    private const int MAX_TOKENS = 128;
    
    private LLamaWeights? _weights;
    private LLamaContext? _context;
    private InteractiveExecutor? _executor;
    private string? _currentModelPath;
    private InferenceStats _lastStats = new();
    private bool _disposed;

    public bool IsModelLoaded => _weights != null && _context != null;
    public string? CurrentModelName { get; private set; }
    public InferenceStats LastStats => _lastStats;

    public event EventHandler<string>? TokenGenerated;
    public event EventHandler<InferenceStats>? StatsUpdated;
    public event EventHandler<string>? StatusChanged;

    /// <summary>
    /// Load a GGUF model from the specified path.
    /// </summary>
    public async Task<bool> LoadModelAsync(string modelPath)
    {
        if (!File.Exists(modelPath))
        {
            StatusChanged?.Invoke(this, $"Model file not found: {modelPath}");
            return false;
        }

        try
        {
            StatusChanged?.Invoke(this, "Loading model...");
            
            // Unload any existing model first
            UnloadModel();

            await Task.Run(() =>
            {
                var parameters = new ModelParams(modelPath)
                {
                    ContextSize = MOBILE_CONTEXT_SIZE,
                    GpuLayerCount = 0,  // CPU only on mobile
                    Threads = Math.Max(1, Environment.ProcessorCount - 1),
                    BatchSize = 64,     // Smaller batch for mobile
                };

                _weights = LLamaWeights.LoadFromFile(parameters);
                _context = _weights.CreateContext(parameters);
                _executor = new InteractiveExecutor(_context);
            });

            _currentModelPath = modelPath;
            CurrentModelName = Path.GetFileNameWithoutExtension(modelPath);
            
            StatusChanged?.Invoke(this, $"Model loaded: {CurrentModelName}");
            return true;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Failed to load model: {ex.Message}");
            Debug.WriteLine($"[MobileLLMService] Load error: {ex}");
            return false;
        }
    }

    /// <summary>
    /// Generate a response stream for the given prompt.
    /// </summary>
    public async IAsyncEnumerable<string> GenerateAsync(
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_executor == null || _context == null)
        {
            yield return "Error: No model loaded. Please select and load a model first.";
            yield break;
        }

        var inferenceParams = new InferenceParams
        {
            MaxTokens = MAX_TOKENS,
            AntiPrompts = new[] { "User:", "\nUser:", "###", "Human:", "\nHuman:" }
        };

        var stopwatch = Stopwatch.StartNew();
        int tokenCount = 0;
        var startMemory = GC.GetTotalMemory(false);

        StatusChanged?.Invoke(this, "Generating...");

        await foreach (var token in _executor.InferAsync(prompt, inferenceParams, cancellationToken))
        {
            tokenCount++;
            
            // Filter unwanted strings
            var cleanToken = token
                .Replace("###", "")
                .Replace("User:", "")
                .Replace("Human:", "")
                .Replace("Assistant:", "");

            if (!string.IsNullOrEmpty(cleanToken))
            {
                TokenGenerated?.Invoke(this, cleanToken);
                yield return cleanToken;
            }

            // Update stats every 5 tokens
            if (tokenCount % 5 == 0)
            {
                UpdateStats(stopwatch.Elapsed, tokenCount, startMemory);
            }
        }

        stopwatch.Stop();
        UpdateStats(stopwatch.Elapsed, tokenCount, startMemory);
        StatusChanged?.Invoke(this, "Complete");
    }

    /// <summary>
    /// Build a prompt with system message and user input.
    /// </summary>
    public static string BuildPrompt(string userMessage, string? systemPrompt = null)
    {
        var sb = new StringBuilder();
        
        var system = systemPrompt ?? "You are a helpful AI assistant. Be concise and direct in your responses.";
        sb.AppendLine($"### System:\n{system}\n");
        sb.AppendLine($"### User:\n{userMessage}\n");
        sb.AppendLine("### Assistant:");
        
        return sb.ToString();
    }

    /// <summary>
    /// Unload the current model and free resources.
    /// </summary>
    public void UnloadModel()
    {
        _executor = null;
        _context?.Dispose();
        _context = null;
        _weights?.Dispose();
        _weights = null;
        _currentModelPath = null;
        CurrentModelName = null;
        
        // Force garbage collection to free memory
        GC.Collect();
        GC.WaitForPendingFinalizers();
        
        StatusChanged?.Invoke(this, "Model unloaded");
    }

    /// <summary>
    /// Clear the context for a new conversation.
    /// </summary>
    public void ClearContext()
    {
        if (_context != null && _weights != null && _currentModelPath != null)
        {
            _context.Dispose();
            var parameters = new ModelParams(_currentModelPath)
            {
                ContextSize = MOBILE_CONTEXT_SIZE,
                GpuLayerCount = 0,
                Threads = Math.Max(1, Environment.ProcessorCount - 1),
            };
            _context = _weights.CreateContext(parameters);
            _executor = new InteractiveExecutor(_context);
            
            StatusChanged?.Invoke(this, "Context cleared");
        }
    }

    private void UpdateStats(TimeSpan elapsed, int tokenCount, long startMemory)
    {
        _lastStats = new InferenceStats
        {
            GeneratedTokens = tokenCount,
            ElapsedTime = elapsed,
            TokensPerSecond = elapsed.TotalSeconds > 0 ? tokenCount / elapsed.TotalSeconds : 0,
            MemoryUsageBytes = GC.GetTotalMemory(false) - startMemory,
            ContextSize = MOBILE_CONTEXT_SIZE
        };

        StatsUpdated?.Invoke(this, _lastStats);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            UnloadModel();
            _disposed = true;
        }
    }
}
