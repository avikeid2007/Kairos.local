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
    private readonly List<LLMModelInfo> _models = new();
    private string _modelsDirectory;
    private LLamaWeights? _loadedWeights;
    private LLMModelInfo? _activeModel;
    
    public IReadOnlyList<LLMModelInfo> Models => _models.AsReadOnly();
    public LLMModelInfo? ActiveModel => _activeModel;
    public string ModelsDirectory => _modelsDirectory;
    
    public event EventHandler<LLMModelInfo>? ModelDownloadStarted;
    public event EventHandler<LLMModelInfo>? ModelDownloadCompleted;
    public event EventHandler<LLMModelInfo>? ModelLoaded;
    public event EventHandler? ModelUnloaded;
    
    public ModelManagerService(IConfiguration configuration, IDownloadService downloadService)
    {
        _configuration = configuration;
        _downloadService = downloadService;
        
        // Use LocalAppData for MSIX compatibility (installation folder is read-only)
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _modelsDirectory = Path.Combine(localAppData, "KaiROS.AI", "Models");
        Directory.CreateDirectory(_modelsDirectory);
    }
    
    public async Task InitializeAsync()
    {
        await Task.Run(() =>
        {
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
        });
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
                    return false;
                }
            }
            else
            {
                model.DownloadState = DownloadState.Paused;
                return false;
            }
        }
        catch (Exception)
        {
            model.DownloadState = DownloadState.Failed;
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
    
    public async Task<bool> SetActiveModelAsync(LLMModelInfo model)
    {
        if (!model.IsDownloaded || model.LocalPath == null)
            return false;
        
        // Unload current model if any
        await UnloadModelAsync();
        
        try
        {
            await Task.Run(() =>
            {
                var parameters = new ModelParams(model.LocalPath)
                {
                    ContextSize = 4096,
                    GpuLayerCount = 35 // Will be adjusted based on hardware
                };
                
                _loadedWeights = LLamaWeights.LoadFromFile(parameters);
            });
            
            _activeModel = model;
            model.IsActive = true;
            ModelLoaded?.Invoke(this, model);
            return true;
        }
        catch
        {
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
}
