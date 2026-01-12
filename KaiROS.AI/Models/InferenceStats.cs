namespace KaiROS.AI.Models;

/// <summary>
/// Inference performance statistics
/// </summary>
public class InferenceStats
{
    public double TokensPerSecond { get; set; }
    public int TotalTokens { get; set; }
    public int PromptTokens { get; set; }
    public int GeneratedTokens { get; set; }
    public TimeSpan ElapsedTime { get; set; }
    public long MemoryUsageBytes { get; set; }
    public string BackendInUse { get; set; } = string.Empty;

    // Context and GPU settings
    public uint ContextSize { get; set; }
    public int GpuLayers { get; set; }

    public string MemoryUsageText => FormatBytes(MemoryUsageBytes);

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "N/A";
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.#} {sizes[order]}";
    }
}
