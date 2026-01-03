namespace KaiROS.AI.Models;

/// <summary>
/// Represents LLM model information from configuration
/// </summary>
public class LLMModelInfo
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SizeText { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
    public string Capabilities { get; set; } = string.Empty;
    public string MinRam { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsRecommended { get; set; }
    
    // Runtime properties (not from config)
    public bool IsDownloaded { get; set; }
    public string? LocalPath { get; set; }
    public double DownloadProgress { get; set; }
    public DownloadState DownloadState { get; set; } = DownloadState.NotStarted;
    public bool IsActive { get; set; }
    public string? LoadError { get; set; }
    
    // Custom model properties
    public bool IsCustomModel { get; set; }
    public int CustomModelId { get; set; }
}

public enum DownloadState
{
    NotStarted,
    Downloading,
    Paused,
    Completed,
    Failed,
    Verifying
}
