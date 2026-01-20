using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KaiROS.Mobile.Models;
using KaiROS.Mobile.Services;
using System.Collections.ObjectModel;

namespace KaiROS.Mobile.ViewModels;

public partial class ModelSelectionViewModel : ObservableObject
{
    private readonly ModelDownloadService _downloadService;
    private readonly MobileLLMService _llmService;

    [ObservableProperty]
    private string _statusText = "Loading models...";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private LLMModelInfo? _selectedModel;

    public ObservableCollection<LLMModelInfo> Models { get; } = new();

    public ModelSelectionViewModel(ModelDownloadService downloadService, MobileLLMService llmService)
    {
        _downloadService = downloadService;
        _llmService = llmService;

        _downloadService.ProgressChanged += (s, progress) =>
            MainThread.BeginInvokeOnMainThread(() => DownloadProgress = progress);
        
        _downloadService.StatusChanged += (s, status) =>
            MainThread.BeginInvokeOnMainThread(() => StatusText = status);
    }

    [RelayCommand]
    private async Task LoadModelsAsync()
    {
        try
        {
            IsLoading = true;
            StatusText = "Loading model catalog...";

            var models = await _downloadService.GetModelCatalogAsync();
            
            Models.Clear();
            foreach (var model in models)
            {
                Models.Add(model);
            }

            StatusText = $"Found {Models.Count} models";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DownloadModelAsync(LLMModelInfo model)
    {
        if (model.IsDownloaded)
        {
            StatusText = "Model already downloaded";
            return;
        }

        try
        {
            IsDownloading = true;
            DownloadProgress = 0;

            var path = await _downloadService.DownloadModelAsync(model);
            
            if (path != null)
            {
                StatusText = $"Downloaded: {model.Name}";
            }
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private void CancelDownload()
    {
        _downloadService.CancelDownload();
        IsDownloading = false;
    }

    [RelayCommand]
    private async Task LoadModelAsync(LLMModelInfo model)
    {
        if (!model.IsDownloaded || string.IsNullOrEmpty(model.LocalPath))
        {
            StatusText = "Model not downloaded yet";
            return;
        }

        try
        {
            IsLoading = true;
            StatusText = $"Loading {model.Name}...";

            var success = await _llmService.LoadModelAsync(model.LocalPath);
            
            if (success)
            {
                StatusText = $"Loaded: {model.Name}";
                SelectedModel = model;
                
                // Navigate to Chat tab
                await Shell.Current.GoToAsync("//Chat");
            }
            else
            {
                StatusText = "Failed to load model";
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DeleteModelAsync(LLMModelInfo model)
    {
        var confirm = await Shell.Current.DisplayAlert("Delete Model", $"Are you sure you want to delete {model.Name}?", "Delete", "Cancel");
        if (!confirm) return;

        if (_downloadService.DeleteModel(model))
        {
            StatusText = $"Deleted: {model.Name}";
        }
    }

    [RelayCommand]
    private void UnloadModel()
    {
        _llmService.UnloadModel();
        SelectedModel = null;
        StatusText = "Model unloaded";
    }
}
