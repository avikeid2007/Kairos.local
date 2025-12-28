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
    
    public SettingsViewModel(IHardwareDetectionService hardwareService, IModelManagerService modelManager, ChatViewModel chatViewModel)
    {
        _hardwareService = hardwareService;
        _modelManager = modelManager;
        _chatViewModel = chatViewModel;
        
        // Initialize system prompt from ChatViewModel
        _systemPrompt = chatViewModel.SystemPrompt;
    }
    
    partial void OnSystemPromptChanged(string value)
    {
        // Sync to ChatViewModel
        _chatViewModel.SystemPrompt = value;
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
            ExecutionBackend.DirectML => Hardware?.HasDirectML == true 
                ? "✓ DirectML: Windows GPU acceleration enabled."
                : "⚠ DirectML not available.",
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
