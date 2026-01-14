namespace KaiROS.AI.Models;

/// <summary>
/// Hardware information and backend detection results
/// </summary>
public class HardwareInfo
{
    public List<ExecutionBackend> AvailableBackends { get; set; } = new();
    public ExecutionBackend RecommendedBackend { get; set; } = ExecutionBackend.Cpu;
    public ExecutionBackend SelectedBackend { get; set; } = ExecutionBackend.Cpu;

    public long TotalRamBytes { get; set; }
    public long AvailableRamBytes { get; set; }
    public string TotalRamText => FormatBytes(TotalRamBytes);
    public string AvailableRamText => FormatBytes(AvailableRamBytes);

    public string? GpuName { get; set; }
    public long GpuMemoryBytes { get; set; }
    public string GpuMemoryText => FormatBytes(GpuMemoryBytes);

    public bool HasCuda { get; set; }
    public bool HasVulkan { get; set; }
    public bool HasNpu { get; set; }

    public string StatusMessage { get; set; } = string.Empty;

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "N/A";
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
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

public enum ExecutionBackend
{
    Cpu,
    Cuda,
    Vulkan,
    Npu,
    Auto
}
