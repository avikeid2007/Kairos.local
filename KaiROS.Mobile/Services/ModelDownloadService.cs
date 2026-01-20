using KaiROS.Mobile.Models;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace KaiROS.Mobile.Services;

/// <summary>
/// Service for downloading and managing GGUF models.
/// </summary>
public class ModelDownloadService
{
    private readonly HttpClient _httpClient;
    private readonly string _modelsDirectory;
    private CancellationTokenSource? _currentDownloadCts;

    public event EventHandler<double>? ProgressChanged;
    public event EventHandler<string>? StatusChanged;

    public ModelDownloadService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("KaiROS-Mobile", "1.0"));

        // Store models in app's local data directory
        _modelsDirectory = Path.Combine(
            FileSystem.Current.AppDataDirectory,
            "Models");

        Directory.CreateDirectory(_modelsDirectory);
    }

    /// <summary>
    /// Get the list of available models from the catalog.
    /// </summary>
    public async Task<List<LLMModelInfo>> GetModelCatalogAsync()
    {
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync("model_catalog.json");
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();

            var models = JsonSerializer.Deserialize<List<LLMModelInfo>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (models != null)
            {
                // Check which models are already downloaded
                foreach (var model in models)
                {
                    var localPath = Path.Combine(_modelsDirectory, model.FileName);
                    if (File.Exists(localPath))
                    {
                        model.LocalPath = localPath;
                    }
                }
            }

            return models ?? new List<LLMModelInfo>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ModelDownloadService] Error loading catalog: {ex}");
            return GetDefaultModels();
        }
    }

    /// <summary>
    /// Download a model from the specified URL with progress tracking.
    /// </summary>
    public async Task<string?> DownloadModelAsync(
        LLMModelInfo model,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(model.DownloadUrl))
        {
            StatusChanged?.Invoke(this, "Invalid download URL");
            return null;
        }

        var destinationPath = Path.Combine(_modelsDirectory, model.FileName);

        // Check if already downloaded
        if (File.Exists(destinationPath))
        {
            model.LocalPath = destinationPath;
            StatusChanged?.Invoke(this, "Model already downloaded");
            return destinationPath;
        }

        try
        {
            _currentDownloadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            StatusChanged?.Invoke(this, $"Downloading {model.Name}...");
            model.IsDownloading = true;
            model.DownloadProgress = 0;

            using var response = await _httpClient.GetAsync(
                model.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                _currentDownloadCts.Token);

            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? model.SizeBytes;
            var tempPath = destinationPath + ".tmp";

            await using var contentStream = await response.Content.ReadAsStreamAsync(_currentDownloadCts.Token);
            await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);

            var buffer = new byte[81920]; // 80KB buffer
            long totalBytesRead = 0;
            int bytesRead;
            var lastProgressUpdate = DateTime.Now;

            while ((bytesRead = await contentStream.ReadAsync(buffer, _currentDownloadCts.Token)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), _currentDownloadCts.Token);
                totalBytesRead += bytesRead;

                // Update progress every 250ms
                if ((DateTime.Now - lastProgressUpdate).TotalMilliseconds > 250)
                {
                    var progress = (double)totalBytesRead / totalBytes;

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        model.DownloadProgress = progress;
                    });

                    ProgressChanged?.Invoke(this, progress);
                    lastProgressUpdate = DateTime.Now;
                }
            }

            // Final update
            MainThread.BeginInvokeOnMainThread(() => model.DownloadProgress = 1.0);

            // Rename temp file to final path
            File.Move(tempPath, destinationPath);

            model.LocalPath = destinationPath;
            model.IsDownloading = false;
            model.DownloadProgress = 1.0;

            StatusChanged?.Invoke(this, $"Download complete: {model.Name}");
            ProgressChanged?.Invoke(this, 1.0);

            return destinationPath;
        }
        catch (OperationCanceledException)
        {
            StatusChanged?.Invoke(this, "Download cancelled");
            model.IsDownloading = false;
            model.DownloadProgress = 0;

            // Clean up partial download
            var tempPath = destinationPath + ".tmp";
            if (File.Exists(tempPath))
                File.Delete(tempPath);

            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ModelDownloadService] Download error: {ex}");
            StatusChanged?.Invoke(this, $"Download failed: {ex.Message}");
            model.IsDownloading = false;
            model.DownloadProgress = 0;
            return null;
        }
    }

    /// <summary>
    /// Cancel the current download.
    /// </summary>
    public void CancelDownload()
    {
        _currentDownloadCts?.Cancel();
    }

    /// <summary>
    /// Delete a downloaded model.
    /// </summary>
    public bool DeleteModel(LLMModelInfo model)
    {
        if (string.IsNullOrEmpty(model.LocalPath) || !File.Exists(model.LocalPath))
            return false;

        try
        {
            File.Delete(model.LocalPath);
            model.LocalPath = null;
            StatusChanged?.Invoke(this, $"Deleted: {model.Name}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ModelDownloadService] Delete error: {ex}");
            return false;
        }
    }

    /// <summary>
    /// Get list of downloaded models.
    /// </summary>
    public List<string> GetDownloadedModels()
    {
        if (!Directory.Exists(_modelsDirectory))
            return new List<string>();

        return Directory.GetFiles(_modelsDirectory, "*.gguf").ToList();
    }

    private static List<LLMModelInfo> GetDefaultModels()
    {
        return new List<LLMModelInfo>
        {
            new()
            {
                Name = "TinyLlama 1.1B Chat Q4",
                FileName = "tinyllama-1.1b-chat-v1.0.Q4_K_M.gguf",
                Description = "Fast, compact model ideal for mobile devices",
                SizeBytes = 669_000_000,
                DownloadUrl = "https://huggingface.co/TheBloke/TinyLlama-1.1B-Chat-v1.0-GGUF/resolve/main/tinyllama-1.1b-chat-v1.0.Q4_K_M.gguf",
                MinRamGB = 2
            },
            new()
            {
                Name = "Qwen2 0.5B Instruct Q4",
                FileName = "qwen2-0_5b-instruct-q4_k_m.gguf",
                Description = "Ultra-compact model for basic tasks",
                SizeBytes = 400_000_000,
                DownloadUrl = "https://huggingface.co/Qwen/Qwen2-0.5B-Instruct-GGUF/resolve/main/qwen2-0_5b-instruct-q4_k_m.gguf",
                MinRamGB = 2
            }
        };
    }
}
