namespace KaiROS.Mobile.Models;

/// <summary>
/// Statistics from LLM inference for performance monitoring.
/// </summary>
public class InferenceStats
{
    public int GeneratedTokens { get; set; }
    public TimeSpan ElapsedTime { get; set; }
    public double TokensPerSecond { get; set; }
    public long MemoryUsageBytes { get; set; }
    public uint ContextSize { get; set; }
    
    public string FormattedSpeed => $"{TokensPerSecond:F1} tok/s";
    public string FormattedTime => ElapsedTime.TotalSeconds < 60 
        ? $"{ElapsedTime.TotalSeconds:F1}s" 
        : $"{ElapsedTime.TotalMinutes:F1}m";
}
