using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KaiROS.AI.Models;
using KaiROS.AI.Services;
using System.Collections.ObjectModel;

namespace KaiROS.AI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IHardwareDetectionService _hardwareService;
    private readonly IModelManagerService _modelManager;
    private readonly ChatViewModel _chatViewModel;
    private readonly IThemeService _themeService;
    private readonly IApiService _apiService;

    private const string DefaultSystemPrompt = "You are a helpful, friendly AI assistant. Be concise and clear.";

    [ObservableProperty]
    private HardwareInfo? _hardware;

    [ObservableProperty]
    private ObservableCollection<ExecutionBackend> _availableBackends = new();

    [ObservableProperty]
    private ExecutionBackend _selectedBackend;

    [ObservableProperty]
    private string _modelsDirectory = string.Empty;

    [ObservableProperty]
    private string _gpuInfo = "Detecting...";

    [ObservableProperty]
    private string _ramInfo = "Detecting...";

    [ObservableProperty]
    private string _backendStatus = string.Empty;

    [ObservableProperty]
    private string _systemPrompt = DefaultSystemPrompt;

    [ObservableProperty]
    private bool _isDarkTheme = true;

    // API Settings
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApiStatus))]
    [NotifyPropertyChangedFor(nameof(IsMinimizeToTrayEnabled))]
    private bool _isApiEnabled = false;

    [ObservableProperty]
    private int _apiPort = 5000;

    // API can only be enabled when a model is loaded
    public bool CanEnableApi => _modelManager.ActiveModel != null;

    // System tray only enabled when API is running
    public bool IsMinimizeToTrayEnabled => IsApiEnabled && _apiService.IsRunning;

    public string ApiStatus => _apiService.IsRunning
        ? $"Running on http://localhost:{_apiService.Port}/"
        : CanEnableApi ? "Stopped (ready to start)" : "Disabled (load a model first)";

    public SettingsViewModel(IHardwareDetectionService hardwareService, IModelManagerService modelManager, ChatViewModel chatViewModel, IThemeService themeService, IApiService apiService)
    {
        _hardwareService = hardwareService;
        _modelManager = modelManager;
        _chatViewModel = chatViewModel;
        _themeService = themeService;
        _apiService = apiService;

        // Initialize system prompt from ChatViewModel
        _systemPrompt = chatViewModel.SystemPrompt;

        // Initialize theme from service
        _isDarkTheme = _themeService.CurrentTheme == "Dark";

        // Initialize API status
        _isApiEnabled = _apiService.IsRunning;

        // Subscribe to model events to update CanEnableApi
        _modelManager.ModelLoaded += (s, e) =>
        {
            OnPropertyChanged(nameof(CanEnableApi));
            OnPropertyChanged(nameof(ApiStatus));
        };
        _modelManager.ModelUnloaded += (s, e) =>
        {
            OnPropertyChanged(nameof(CanEnableApi));
            OnPropertyChanged(nameof(ApiStatus));
            // Disable API if model is unloaded
            if (IsApiEnabled)
            {
                IsApiEnabled = false;
            }
        };
    }

    partial void OnSystemPromptChanged(string value)
    {
        // Sync to ChatViewModel
        _chatViewModel.SystemPrompt = value;
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        _themeService.SetTheme(value ? "Dark" : "Light");

        // Show message that restart is needed
        System.Windows.MessageBox.Show(
            "Theme preference saved. Please restart the application for the theme to take effect.",
            "Theme Changed",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    async partial void OnIsApiEnabledChanged(bool value)
    {
        if (value)
        {
            await _apiService.StartAsync(ApiPort);
            OnPropertyChanged(nameof(ApiStatus));
        }
        else
        {
            await _apiService.StopAsync();
            OnPropertyChanged(nameof(ApiStatus));
        }
    }

    public override async Task InitializeAsync()
    {
        IsLoading = true;

        try
        {
            Hardware = await _hardwareService.DetectHardwareAsync();

            AvailableBackends.Clear();
            foreach (var backend in Hardware.AvailableBackends)
            {
                AvailableBackends.Add(backend);
            }

            SelectedBackend = Hardware.SelectedBackend;
            ModelsDirectory = _modelManager.ModelsDirectory;

            GpuInfo = !string.IsNullOrEmpty(Hardware.GpuName)
                ? $"{Hardware.GpuName} ({Hardware.GpuMemoryText})"
                : "No dedicated GPU detected";

            RamInfo = $"{Hardware.TotalRamText} total, {Hardware.AvailableRamText} available";

            UpdateBackendStatus();
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedBackendChanged(ExecutionBackend value)
    {
        if (Hardware != null)
        {
            Hardware.SelectedBackend = value;
            // Also update the service's cached copy so model loading respects this selection
            _hardwareService.SetSelectedBackend(value);
            UpdateBackendStatus();
        }
    }

    private void UpdateBackendStatus()
    {
        BackendStatus = SelectedBackend switch
        {
            ExecutionBackend.Cpu => "✓ CPU mode: Compatible with all systems. Slower but reliable.",
            ExecutionBackend.Cuda => Hardware?.HasCuda == true
                ? "✓ CUDA: NVIDIA GPU acceleration enabled."
                : "⚠ CUDA not available. Install CUDA toolkit.",
            ExecutionBackend.Vulkan => Hardware?.HasVulkan == true
                ? "✓ Vulkan: High-performance GPU acceleration enabled (Best for Intel Arc/AMD)."
                : "⚠ Vulkan not available.",
            ExecutionBackend.Npu => Hardware?.HasNpu == true
                ? "✓ NPU: Neural processing unit detected."
                : "⚠ NPU not available on this system.",
            _ => "Select a backend"
        };
    }

    [RelayCommand]
    private void BrowseModelsDirectory()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Models Directory"
        };

        if (dialog.ShowDialog() == true)
        {
            ModelsDirectory = dialog.FolderName;
            _modelManager.SetModelsDirectory(dialog.FolderName);
        }
    }

    [RelayCommand]
    private void UseRecommendedBackend()
    {
        if (Hardware != null)
        {
            SelectedBackend = Hardware.RecommendedBackend;
        }
    }

    [RelayCommand]
    private async Task RefreshHardwareInfo()
    {
        _hardwareService.ClearCache();
        await InitializeAsync();
    }

    [RelayCommand]
    private void ResetSystemPrompt()
    {
        SystemPrompt = DefaultSystemPrompt;
    }
}
