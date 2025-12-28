using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KaiROS.AI.Models;
using KaiROS.AI.Services;
using System.Collections.ObjectModel;

namespace KaiROS.AI.ViewModels;

public partial class ModelCatalogViewModel : ViewModelBase
{
    private readonly IModelManagerService _modelManager;
    private readonly Dictionary<string, CancellationTokenSource> _downloadCts = new();
    
    public event EventHandler? ModelActivated;
    
    [ObservableProperty]
    private ObservableCollection<ModelItemViewModel> _models = new();
    
    [ObservableProperty]
    private ObservableCollection<ModelItemViewModel> _filteredModels = new();
    
    [ObservableProperty]
    private string _selectedCategory = "all";
    
    [ObservableProperty]
    private bool _showRecommendedOnly;
    
    [ObservableProperty]
    private string _searchText = string.Empty;
    
    public ModelCatalogViewModel(IModelManagerService modelManager)
    {
        _modelManager = modelManager;
    }
    
    public override async Task InitializeAsync()
    {
        IsLoading = true;
        
        try
        {
            Models.Clear();
            foreach (var model in _modelManager.Models)
            {
                var vm = new ModelItemViewModel(model, this);
                Models.Add(vm);
            }
            
            ApplyFilters();
        }
        finally
        {
            IsLoading = false;
        }
        
        await Task.CompletedTask;
    }
    
    partial void OnSelectedCategoryChanged(string value) => ApplyFilters();
    partial void OnShowRecommendedOnlyChanged(bool value) => ApplyFilters();
    partial void OnSearchTextChanged(string value) => ApplyFilters();
    
    private void ApplyFilters()
    {
        var filtered = Models.AsEnumerable();
        
        if (SelectedCategory != "all")
        {
            filtered = filtered.Where(m => m.Model.Category.Equals(SelectedCategory, StringComparison.OrdinalIgnoreCase));
        }
        
        if (ShowRecommendedOnly)
        {
            filtered = filtered.Where(m => m.Model.IsRecommended);
        }
        
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            filtered = filtered.Where(m => 
                m.Model.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                m.Model.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }
        
        FilteredModels = new ObservableCollection<ModelItemViewModel>(filtered);
    }
    
    [RelayCommand]
    private void FilterByCategory(string category)
    {
        SelectedCategory = category;
    }
    
    [RelayCommand]
    private void ToggleRecommendedFilter()
    {
        ShowRecommendedOnly = !ShowRecommendedOnly;
    }
    
    public async Task DownloadModelAsync(ModelItemViewModel modelVm)
    {
        var model = modelVm.Model;
        var cts = new CancellationTokenSource();
        _downloadCts[model.Name] = cts;
        
        // Update UI state on UI thread
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            modelVm.IsDownloading = true;
            modelVm.IsPaused = false;
        });
        
        try
        {
            var progress = new Progress<double>(p =>
            {
                // Progress callbacks happen on UI thread when using Progress<T>
                modelVm.DownloadProgress = p;
            });
            
            var success = await _modelManager.DownloadModelAsync(model, progress, cts.Token);
            
            // Update final state on UI thread
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                modelVm.IsDownloading = false;
                // Set IsDownloaded based on the return value, not the model property
                modelVm.IsDownloaded = success;
                if (success)
                {
                    modelVm.DownloadProgress = 100;
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Paused - don't set IsDownloading to false, let pause handler do it
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                modelVm.IsDownloading = false;
                modelVm.IsPaused = true;
            });
        }
        catch (Exception ex)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                modelVm.IsDownloading = false;
                modelVm.ErrorMessage = ex.Message;
            });
        }
        finally
        {
            _downloadCts.Remove(model.Name);
        }
    }
    
    public async Task PauseDownloadAsync(ModelItemViewModel modelVm)
    {
        if (_downloadCts.TryGetValue(modelVm.Model.Name, out var cts))
        {
            cts.Cancel();
        }
        await _modelManager.PauseDownloadAsync(modelVm.Model);
        modelVm.IsPaused = true;
    }
    
    public async Task ResumeDownloadAsync(ModelItemViewModel modelVm)
    {
        modelVm.IsPaused = false;
        await DownloadModelAsync(modelVm);
    }
    
    public async Task SetActiveModelAsync(ModelItemViewModel modelVm)
    {
        modelVm.IsLoading = true;
        
        try
        {
            foreach (var m in Models)
            {
                m.IsActive = false;
            }
            
            var success = await _modelManager.SetActiveModelAsync(modelVm.Model);
            modelVm.IsActive = success;
            
            if (success)
            {
                // Navigate to Chat after model loads
                ModelActivated?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                modelVm.ErrorMessage = "Failed to load model";
            }
        }
        finally
        {
            modelVm.IsLoading = false;
        }
    }
    
    public async Task DeleteModelAsync(ModelItemViewModel modelVm)
    {
        var result = System.Windows.MessageBox.Show(
            $"Delete {modelVm.Model.DisplayName}? This will remove the downloaded file.",
            "Confirm Delete",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        
        if (result == System.Windows.MessageBoxResult.Yes)
        {
            await _modelManager.DeleteModelAsync(modelVm.Model);
            modelVm.IsDownloaded = false;
            modelVm.IsActive = false;
            modelVm.DownloadProgress = 0;
        }
    }
}

public partial class ModelItemViewModel : ObservableObject
{
    private readonly ModelCatalogViewModel _parent;
    
    public LLMModelInfo Model { get; }
    
    [ObservableProperty]
    private bool _isDownloaded;
    
    [ObservableProperty]
    private bool _isDownloading;
    
    [ObservableProperty]
    private bool _isPaused;
    
    [ObservableProperty]
    private bool _isActive;
    
    [ObservableProperty]
    private bool _isLoading;
    
    [ObservableProperty]
    private double _downloadProgress;
    
    [ObservableProperty]
    private string? _errorMessage;
    
    public ModelItemViewModel(LLMModelInfo model, ModelCatalogViewModel parent)
    {
        Model = model;
        _parent = parent;
        _isDownloaded = model.IsDownloaded;
        _downloadProgress = model.DownloadProgress;
        _isActive = model.IsActive;
    }
    
    [RelayCommand]
    private async Task Download() => await _parent.DownloadModelAsync(this);
    
    [RelayCommand]
    private async Task Pause() => await _parent.PauseDownloadAsync(this);
    
    [RelayCommand]
    private async Task Resume() => await _parent.ResumeDownloadAsync(this);
    
    [RelayCommand]
    private async Task SetActive() => await _parent.SetActiveModelAsync(this);
    
    [RelayCommand]
    private async Task Delete() => await _parent.DeleteModelAsync(this);
}
