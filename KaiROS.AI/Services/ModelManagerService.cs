using KaiROS.AI.Models;
using Microsoft.Extensions.Configuration;
using LLama;
using LLama.Common;
using System.IO;

namespace KaiROS.AI.Services;

public class ModelManagerService : IModelManagerService
{
    private readonly IDownloadService _downloadService;
    private readonly IConfiguration _configuration;
    private readonly IDatabaseService _databaseService;
    private readonly IHardwareDetectionService _hardwareService;
    private readonly List<LLMModelInfo> _models = new();
    private string _modelsDirectory;
    private LLamaWeights? _loadedWeights;
    private LLMModelInfo? _activeModel;
    private int _currentGpuLayers;

    public IReadOnlyList<LLMModelInfo> Models => _models.AsReadOnly();
    public LLMModelInfo? ActiveModel => _activeModel;
    public string ModelsDirectory => _modelsDirectory;
    public int CurrentGpuLayers => _currentGpuLayers;

    public event EventHandler<LLMModelInfo>? ModelDownloadStarted;
    public event EventHandler<LLMModelInfo>? ModelDownloadCompleted;
    public event EventHandler<LLMModelInfo>? ModelLoaded;
    public event EventHandler? ModelUnloaded;
    public event EventHandler<double>? ModelLoadProgress;

    public ModelManagerService(IConfiguration configuration, IDownloadService downloadService, IDatabaseService databaseService, IHardwareDetectionService hardwareService)
    {
        _configuration = configuration;
        _downloadService = downloadService;
        _databaseService = databaseService;
        _hardwareService = hardwareService;

        // Use LocalAppData for MSIX compatibility (installation folder is read-only)
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _modelsDirectory = Path.Combine(localAppData, "KaiROS.AI", "Models");
        Directory.CreateDirectory(_modelsDirectory);
    }

    public async Task InitializeAsync()
    {
        // Initialize database
        await _databaseService.InitializeAsync();

        // Load model catalog from configuration
        var modelConfigs = _configuration.GetSection("LLMModels").Get<List<LLMModelInfo>>() ?? new();

        _models.Clear();
        foreach (var model in modelConfigs)
        {
            // Check if model is already downloaded
            var localPath = Path.Combine(_modelsDirectory, model.Name);
            model.LocalPath = localPath;
            model.IsDownloaded = File.Exists(localPath);

            if (model.IsDownloaded)
            {
                model.DownloadState = DownloadState.Completed;
                model.DownloadProgress = 100;
            }
            else if (_downloadService.HasPartialDownload(model.Name))
            {
                model.DownloadState = DownloadState.Paused;
            }

            _models.Add(model);
        }

        // Load custom models from SQLite
        var customModels = await _databaseService.GetCustomModelsAsync();
        foreach (var custom in customModels)
        {
            var model = new LLMModelInfo
            {
                Name = custom.Name,
                DisplayName = custom.DisplayName,
                Description = custom.Description,
                DownloadUrl = custom.DownloadUrl,
                LocalPath = custom.IsLocal ? custom.FilePath : Path.Combine(_modelsDirectory, custom.Name),
                SizeBytes = custom.SizeBytes,
                IsDownloaded = custom.IsLocal || File.Exists(Path.Combine(_modelsDirectory, custom.Name)),
                IsCustomModel = true,
                CustomModelId = custom.Id,
                Organization = "Local",
                OrgLogoUrl = "pack://application:,,,/Assets/logo.png",
                Family = "Custom",
                Variant = "All"
            };

            if (model.IsDownloaded)
            {
                model.DownloadState = DownloadState.Completed;
                model.DownloadProgress = 100;
            }

            _models.Add(model);
        }
    }

    public async Task<bool> DownloadModelAsync(LLMModelInfo model, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (model.IsDownloaded) return true;

        var localPath = Path.Combine(_modelsDirectory, model.Name);
        model.DownloadState = DownloadState.Downloading;
        ModelDownloadStarted?.Invoke(this, model);

        try
        {
            var wrappedProgress = new Progress<double>(p =>
            {
                model.DownloadProgress = p;
                progress?.Report(p);
            });

            var success = await _downloadService.DownloadFileAsync(
                model.DownloadUrl,
                localPath,
                wrappedProgress,
                cancellationToken);

            if (success)
            {
                model.DownloadState = DownloadState.Verifying;
                var valid = await _downloadService.VerifyFileIntegrityAsync(localPath, model.SizeBytes);

                if (valid)
                {
                    model.IsDownloaded = true;
                    model.LocalPath = localPath;
                    model.DownloadState = DownloadState.Completed;
                    model.DownloadProgress = 100;
                    ModelDownloadCompleted?.Invoke(this, model);
                    return true;
                }
                else
                {
                    model.DownloadState = DownloadState.Failed;
                    model.LoadError = "File verification failed. The download may be corrupted - please try again.";
                    System.Diagnostics.Debug.WriteLine($"Verification failed for {model.Name} at {localPath}");
                    return false;
                }
            }
            else
            {
                model.DownloadState = DownloadState.Paused;
                return false;
            }
        }
        catch (Exception ex)
        {
            model.DownloadState = DownloadState.Failed;
            model.LoadError = ex.Message;
            throw;
        }
    }

    public async Task PauseDownloadAsync(LLMModelInfo model)
    {
        await _downloadService.PauseDownloadAsync(model.Name);
        model.DownloadState = DownloadState.Paused;
    }

    public async Task ResumeDownloadAsync(LLMModelInfo model)
    {
        await _downloadService.ResumeDownloadAsync(model.Name);
    }

    public async Task<bool> DeleteModelAsync(LLMModelInfo model)
    {
        if (_activeModel?.Name == model.Name)
        {
            await UnloadModelAsync();
        }

        try
        {
            if (model.LocalPath != null && File.Exists(model.LocalPath))
            {
                await Task.Run(() => File.Delete(model.LocalPath));
            }

            var partialPath = Path.Combine(_modelsDirectory, model.Name + ".partial");
            if (File.Exists(partialPath))
            {
                await Task.Run(() => File.Delete(partialPath));
            }

            model.IsDownloaded = false;
            model.DownloadState = DownloadState.NotStarted;
            model.DownloadProgress = 0;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> SetActiveModelAsync(LLMModelInfo model, IProgress<double>? progress = null)
    {
        if (!model.IsDownloaded || model.LocalPath == null)
            return false;

        if (!File.Exists(model.LocalPath))
        {
            model.IsDownloaded = false;
            return false;
        }

        // Report initial progress
        progress?.Report(5);
        ModelLoadProgress?.Invoke(this, 5);

        // Unload current model if any
        await UnloadModelAsync();

        progress?.Report(10);
        ModelLoadProgress?.Invoke(this, 10);

        try
        {
            // Get hardware info for GPU detection
            var hardwareInfo = await _hardwareService.DetectHardwareAsync();

            // Calculate optimal GPU layers based on VRAM and model size
            _currentGpuLayers = CalculateOptimalGpuLayers(hardwareInfo, model);

            System.Diagnostics.Debug.WriteLine($"[KaiROS] Loading model with backend: {hardwareInfo.SelectedBackend}, GpuLayerCount: {_currentGpuLayers}");

            // Try loading with decreasing GPU layers on failure
            int[] layersToTry = new[]
            {
                _currentGpuLayers,
                Math.Max(1, _currentGpuLayers / 2),  // 50%
                Math.Max(1, _currentGpuLayers / 4),  // 25%
                0  // CPU fallback
            };

            Exception? lastException = null;

            foreach (var layers in layersToTry)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"[KaiROS] Attempting to load with {layers} GPU layers...");

                    await Task.Run(() =>
                    {
                        progress?.Report(20);
                        ModelLoadProgress?.Invoke(this, 20);

                        var parameters = new ModelParams(model.LocalPath)
                        {
                            ContextSize = 4096,
                            GpuLayerCount = layers
                        };

                        progress?.Report(30);
                        ModelLoadProgress?.Invoke(this, 30);

                        // This is the heavy operation - loading weights
                        _loadedWeights = LLamaWeights.LoadFromFile(parameters);

                        progress?.Report(90);
                        ModelLoadProgress?.Invoke(this, 90);
                    });

                    // Success!
                    _currentGpuLayers = layers;
                    _activeModel = model;
                    model.IsActive = true;

                    progress?.Report(100);
                    ModelLoadProgress?.Invoke(this, 100);

                    if (layers < layersToTry[0])
                    {
                        System.Diagnostics.Debug.WriteLine($"[KaiROS] Model loaded successfully with reduced layers: {layers} (original: {layersToTry[0]})");
                    }

                    ModelLoaded?.Invoke(this, model);
                    return true;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    System.Diagnostics.Debug.WriteLine($"[KaiROS] Failed to load with {layers} layers: {ex.Message}");

                    // Clean up any partial state
                    if (_loadedWeights != null)
                    {
                        try { _loadedWeights.Dispose(); } catch { }
                        _loadedWeights = null;
                    }

                    // If this was already CPU fallback (0 layers), don't retry
                    if (layers == 0) break;
                }
            }

            // All attempts failed
            System.Diagnostics.Debug.WriteLine($"Error loading model: {lastException?.Message}");
            model.LoadError = lastException?.Message ?? "Failed to load model after multiple attempts";
            progress?.Report(0);
            ModelLoadProgress?.Invoke(this, 0);
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading model: {ex.Message}");
            // Store error for UI to display
            model.LoadError = ex.Message;
            progress?.Report(0);
            ModelLoadProgress?.Invoke(this, 0);
            return false;
        }
    }

    public async Task UnloadModelAsync()
    {
        if (_loadedWeights != null)
        {
            await Task.Run(() =>
            {
                _loadedWeights.Dispose();
                _loadedWeights = null;
            });

            if (_activeModel != null)
            {
                _activeModel.IsActive = false;
                _activeModel = null;
            }

            ModelUnloaded?.Invoke(this, EventArgs.Empty);
            GC.Collect();
        }
    }

    public async Task<bool> VerifyModelAsync(LLMModelInfo model)
    {
        if (model.LocalPath == null) return false;
        return await _downloadService.VerifyFileIntegrityAsync(model.LocalPath, model.SizeBytes);
    }

    public void SetModelsDirectory(string path)
    {
        _modelsDirectory = path;
        Directory.CreateDirectory(_modelsDirectory);
    }

    internal LLamaWeights? GetLoadedWeights() => _loadedWeights;

    /// <summary>
    /// Calculate optimal GPU layers based on available VRAM and model size.
    /// Uses conservative estimates to prevent OOM crashes.
    /// </summary>
    private int CalculateOptimalGpuLayers(HardwareInfo hardwareInfo, LLMModelInfo model)
    {
        // CPU-only or NPU modes don't use GPU layers
        if (hardwareInfo.SelectedBackend == ExecutionBackend.Cpu ||
            hardwareInfo.SelectedBackend == ExecutionBackend.Npu)
        {
            return 0;
        }

        // Get available VRAM in GB
        double vramGB = hardwareInfo.GpuMemoryBytes / (1024.0 * 1024.0 * 1024.0);

        // Get model size in GB
        double modelSizeGB = model.SizeBytes / (1024.0 * 1024.0 * 1024.0);
        if (modelSizeGB <= 0) modelSizeGB = 4.0; // Default assumption for unknown size

        // Estimate total layers based on model size (Q4_K_M quantization typical values)
        int estimatedTotalLayers = modelSizeGB switch
        {
            < 1.0 => 22,    // TinyLlama ~1B
            < 2.0 => 24,    // Phi-2 ~2.7B
            < 3.0 => 26,    // Phi-3 Mini ~3.8B, LLaMA 3.2 3B
            < 5.0 => 32,    // Mistral 7B, LLaMA 3.1 8B
            < 8.0 => 40,    // 13B models
            _ => 60         // Larger models
        };

        System.Diagnostics.Debug.WriteLine($"[GPU] VRAM: {vramGB:F1} GB, Model: {modelSizeGB:F1} GB, Est. layers: {estimatedTotalLayers}");

        // Calculate how many layers can fit in VRAM
        // Rule of thumb: Each layer uses approximately (ModelSize / TotalLayers) * 1.2 (20% overhead)
        double memoryPerLayerGB = (modelSizeGB / estimatedTotalLayers) * 1.2;

        // Reserve 1.5GB VRAM for context, KV cache, and system overhead
        double availableForLayersGB = Math.Max(0, vramGB - 1.5);

        int maxLayersByVram = (int)(availableForLayersGB / memoryPerLayerGB);

        // Take minimum of estimated total layers and what fits in VRAM
        int optimalLayers = Math.Min(estimatedTotalLayers, maxLayersByVram);

        // Ensure we have at least some GPU acceleration if VRAM allows
        if (optimalLayers < 5 && vramGB >= 2.0)
        {
            optimalLayers = 5; // Minimum useful GPU acceleration
        }

        // Cap at reasonable maximum to leave room for other operations
        optimalLayers = Math.Min(optimalLayers, 100);

        // Never go below 0
        optimalLayers = Math.Max(optimalLayers, 0);

        System.Diagnostics.Debug.WriteLine($"[GPU] Calculated optimal layers: {optimalLayers} (max by VRAM: {maxLayersByVram})");

        return optimalLayers;
    }
}
