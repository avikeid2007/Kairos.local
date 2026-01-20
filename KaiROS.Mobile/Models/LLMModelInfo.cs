namespace KaiROS.Mobile.Models;

/// <summary>
/// Information about an LLM model available for download or loaded locally.
/// </summary>
public class LLMModelInfo
{
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
    public int MinRamGB { get; set; } = 2;
    public string? LocalPath { get; set; }
    public bool IsDownloaded => !string.IsNullOrEmpty(LocalPath) && File.Exists(LocalPath);
    public double DownloadProgress { get; set; }
    public bool IsDownloading { get; set; }
    
    /// <summary>
    /// Formatted size string for display (e.g., "1.2 GB")
    /// </summary>
    public string FormattedSize
    {
        get
        {
            if (SizeBytes >= 1_000_000_000)
                return $"{SizeBytes / 1_000_000_000.0:F1} GB";
            if (SizeBytes >= 1_000_000)
                return $"{SizeBytes / 1_000_000.0:F1} MB";
            return $"{SizeBytes / 1_000.0:F1} KB";
        }
    }
}
