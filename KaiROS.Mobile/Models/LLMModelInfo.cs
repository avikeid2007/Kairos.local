using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace KaiROS.Mobile.Models;

/// <summary>
/// Information about an LLM model available for download or loaded locally.
/// </summary>
public partial class LLMModelInfo : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
    public int MinRamGB { get; set; } = 2;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloaded))]
    private string? _localPath;

    public bool IsDownloaded => !string.IsNullOrEmpty(LocalPath) && File.Exists(LocalPath);

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private bool _isDownloading;

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
