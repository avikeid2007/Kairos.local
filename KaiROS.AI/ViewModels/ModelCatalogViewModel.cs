using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KaiROS.AI.Models;
using KaiROS.AI.Services;
using KaiROS.AI.Views;
using System.Collections.ObjectModel;

namespace KaiROS.AI.ViewModels;

public partial class ModelCatalogViewModel : ViewModelBase
{
    private readonly IModelManagerService _modelManager;
    private readonly IDatabaseService _databaseService;
    private readonly IHardwareDetectionService _hardwareService;
    private readonly Dictionary<string, CancellationTokenSource> _downloadCts = new();

    public event EventHandler? ModelActivated;

    // Expose the selected backend name for child ViewModels
    public string SelectedBackendName => _hardwareService.GetRecommendedBackend().ToString();

    public async Task<string> GetSelectedBackendNameAsync()
    {
        var hw = await _hardwareService.DetectHardwareAsync();
        return hw.SelectedBackend.ToString();
    }

    [ObservableProperty]
    private ObservableCollection<ModelItemViewModel> _models = new();

    [ObservableProperty]
    private ObservableCollection<ModelItemViewModel> _filteredModels = new();

    [ObservableProperty]
    private ObservableCollection<ModelItemViewModel> _downloadedModels = new();

    [ObservableProperty]
    private ObservableCollection<OrganizationGroup> _groupedModels = new();

    [ObservableProperty]
    private string _selectedCategory = "all";

    [ObservableProperty]
    private bool _showRecommendedOnly;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedOrganization = "all";

    [ObservableProperty]
    private string _selectedFamily = "all";

    [ObservableProperty]
    private string _selectedVariant = "all";

    // Filter dropdown collections
    public ObservableCollection<string> Organizations { get; } = new() { "all" };
    public ObservableCollection<string> Families { get; } = new() { "all" };
    public ObservableCollection<string> Variants { get; } = new() { "all", "All", "CPU-Only", "GPU-Recommended" };

    public ModelCatalogViewModel(IModelManagerService modelManager, IDatabaseService databaseService, IHardwareDetectionService hardwareService)
    {
        _modelManager = modelManager;
        _databaseService = databaseService;
        _hardwareService = hardwareService;
    }

    public override async Task InitializeAsync()
    {
        IsLoading = true;

        try
        {
            Models.Clear();
            Organizations.Clear();
            Organizations.Add("all");
            Families.Clear();
            Families.Add("all");

            foreach (var model in _modelManager.Models)
            {
                var vm = new ModelItemViewModel(model, this);
                Models.Add(vm);

                // Build filter collections dynamically
                if (!string.IsNullOrEmpty(model.Organization) && !Organizations.Contains(model.Organization))
                    Organizations.Add(model.Organization);
                if (!string.IsNullOrEmpty(model.Family) && !Families.Contains(model.Family))
                    Families.Add(model.Family);
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
    partial void OnSelectedOrganizationChanged(string value) => ApplyFilters();
    partial void OnSelectedFamilyChanged(string value) => ApplyFilters();
    partial void OnSelectedVariantChanged(string value) => ApplyFilters();

    private void ApplyFilters()
    {
        var filtered = Models.AsEnumerable();

        if (SelectedCategory != "all")
        {
            filtered = filtered.Where(m => m.Model.Category.Equals(SelectedCategory, StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedOrganization != "all")
        {
            filtered = filtered.Where(m => m.Model.Organization.Equals(SelectedOrganization, StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedFamily != "all")
        {
            filtered = filtered.Where(m => m.Model.Family.Equals(SelectedFamily, StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedVariant != "all")
        {
            filtered = filtered.Where(m => m.Model.Variant.Equals(SelectedVariant, StringComparison.OrdinalIgnoreCase));
        }

        if (ShowRecommendedOnly)
        {
            filtered = filtered.Where(m => m.Model.IsRecommended);
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            filtered = filtered.Where(m =>
                m.Model.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                m.Model.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                m.Model.Organization.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                m.Model.Family.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        FilteredModels = new ObservableCollection<ModelItemViewModel>(filtered);

        // Update downloaded models
        DownloadedModels = new ObservableCollection<ModelItemViewModel>(
            filtered.Where(m => m.IsDownloaded).OrderByDescending(m => m.IsActive));

        // Custom organization ordering: Meta -> Microsoft -> Google -> then alphabetically
        var orgPriority = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Meta", 0 },
            { "Microsoft", 1 },
            { "Google", 2 }
        };

        // Group by organization with custom order, models sorted by size within each group
        var groups = filtered
            .GroupBy(m => m.Model.Organization)
            .OrderBy(g => orgPriority.TryGetValue(g.Key, out var priority) ? priority : 100)
            .ThenBy(g => g.Key)
            .Select(g => new OrganizationGroup(
                g.Key,
                g.First().Model.OrgLogoUrl,
                new ObservableCollection<ModelItemViewModel>(g.OrderBy(m => m.Model.SizeBytes))))
            .ToList();

        GroupedModels = new ObservableCollection<OrganizationGroup>(groups);
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
            // IMPORTANT: Set IsDownloaded BEFORE IsDownloading=false to ensure UI triggers work
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (success)
                {
                    modelVm.DownloadProgress = 100;
                    modelVm.ErrorMessage = null;
                    // Set IsDownloaded first, then clear downloading flag
                    modelVm.IsDownloaded = true;
                    modelVm.IsDownloading = false;
                }
                else
                {
                    modelVm.IsDownloading = false;
                    modelVm.IsDownloaded = false;
                    // Show error if verification failed
                    modelVm.ErrorMessage = model.LoadError ?? "Download failed. Please try again.";
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
        modelVm.LoadingProgress = 0;
        modelVm.ErrorMessage = null; // Clear previous error

        try
        {
            foreach (var m in Models)
            {
                m.IsActive = false;
            }

            // Create progress callback to update UI
            var progress = new Progress<double>(p =>
            {
                modelVm.LoadingProgress = p;
            });

            var success = await _modelManager.SetActiveModelAsync(modelVm.Model, progress);
            modelVm.IsActive = success;

            if (success)
            {
                // Navigate to Chat after model loads
                ModelActivated?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                // Show specific error if available
                var error = modelVm.Model.LoadError;
                modelVm.ErrorMessage = !string.IsNullOrEmpty(error)
                    ? $"Failed to load: {error}"
                    : "Failed to load model. The file may be corrupted - try deleting and re-downloading.";
            }
        }
        catch (Exception ex)
        {
            modelVm.ErrorMessage = $"Error: {ex.Message}";
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
            // If it's a custom model, also delete from database
            if (modelVm.Model.IsCustomModel)
            {
                await _databaseService.DeleteCustomModelAsync(modelVm.Model.CustomModelId);
                Models.Remove(modelVm);
                FilteredModels.Remove(modelVm);
            }

            await _modelManager.DeleteModelAsync(modelVm.Model);
            modelVm.IsDownloaded = false;
            modelVm.IsActive = false;
            modelVm.DownloadProgress = 0;
        }
    }

    [RelayCommand]
    private async Task AddCustomModelAsync()
    {
        var dialog = new AddCustomModelDialog
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            // Save to database
            await _databaseService.AddCustomModelAsync(dialog.Result);

            // Refresh the model list
            await InitializeAsync();

            System.Windows.MessageBox.Show(
                $"Model '{dialog.Result.DisplayName}' added successfully!",
                "Model Added",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
    }
}

public partial class ModelItemViewModel : ObservableObject
{
    private readonly ModelCatalogViewModel _parent;

    public LLMModelInfo Model { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoadButton))]
    private bool _isDownloaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoadButton))]
    private bool _isDownloading;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadingText))]
    private bool _isLoading;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private string? _errorMessage;

    // Computed property that triggers UI update
    public bool ShowLoadButton => IsDownloaded && !IsDownloading;

    // Loading text that shows backend type and progress
    public string LoadingText
    {
        get
        {
            if (!IsLoading) return "";

            // Use the recommended backend from the service (no async wait)
            var backend = _parent.SelectedBackendName;

            if (LoadingProgress > 0 && LoadingProgress < 100)
                return $"Loading on {backend}... {LoadingProgress:0}%";
            else
                return $"Loading on {backend}...";
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadingText))]
    private double _loadingProgress;

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

/// <summary>
/// Represents a group of models organized by their organization/publisher
/// </summary>
public partial class OrganizationGroup : ObservableObject
{
    public string OrganizationName { get; }
    public string LogoUrl { get; }
    public ObservableCollection<ModelItemViewModel> Models { get; }
    public int ModelCount => Models.Count;
    public int DownloadedCount => Models.Count(m => m.IsDownloaded);

    [ObservableProperty]
    private bool _isExpanded = true;

    public OrganizationGroup(string name, string logoUrl, ObservableCollection<ModelItemViewModel> models)
    {
        OrganizationName = name;
        LogoUrl = logoUrl;
        Models = models;
    }
}
