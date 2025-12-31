using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KaiROS.AI.Models;
using KaiROS.AI.Services;
using System.Collections.ObjectModel;

namespace KaiROS.AI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IModelManagerService _modelManager;
    private readonly IHardwareDetectionService _hardwareService;
    
    [ObservableProperty]
    private ViewModelBase? _currentView;
    
    [ObservableProperty]
    private string _statusText = "Ready";
    
    [ObservableProperty]
    private string _hardwareInfo = "Detecting hardware...";
    
    [ObservableProperty]
    private string? _activeModelName;
    
    [ObservableProperty]
    private HardwareInfo? _hardware;
    
    [ObservableProperty]
    private int _selectedNavigationIndex;
    
    public ModelCatalogViewModel CatalogViewModel { get; }
    public ChatViewModel ChatViewModel { get; }
    public SettingsViewModel SettingsViewModel { get; }
    public DocumentViewModel DocumentViewModel { get; }
    
    public MainViewModel(
        IModelManagerService modelManager,
        IHardwareDetectionService hardwareService,
        ModelCatalogViewModel catalogViewModel,
        ChatViewModel chatViewModel,
        SettingsViewModel settingsViewModel,
        DocumentViewModel documentViewModel)
    {
        _modelManager = modelManager;
        _hardwareService = hardwareService;
        CatalogViewModel = catalogViewModel;
        ChatViewModel = chatViewModel;
        SettingsViewModel = settingsViewModel;
        DocumentViewModel = documentViewModel;
        
        _modelManager.ModelLoaded += (s, m) =>
        {
            ActiveModelName = m.DisplayName;
            StatusText = $"Model loaded: {m.DisplayName}";
        };
        
        _modelManager.ModelUnloaded += (s, e) =>
        {
            ActiveModelName = null;
            StatusText = "Model unloaded";
        };
        
        // Auto-navigate to Chat when model is loaded
        CatalogViewModel.ModelActivated += (s, e) =>
        {
            SelectedNavigationIndex = 1; // Navigate to Chat
        };
    }
    
    public override async Task InitializeAsync()
    {
        IsLoading = true;
        StatusText = "Initializing...";
        
        try
        {
            // Detect hardware
            Hardware = await _hardwareService.DetectHardwareAsync();
            HardwareInfo = Hardware.StatusMessage;
            
            // Initialize model catalog
            await _modelManager.InitializeAsync();
            
            // Initialize child view models
            await CatalogViewModel.InitializeAsync();
            await ChatViewModel.InitializeAsync();
            await SettingsViewModel.InitializeAsync();
            await DocumentViewModel.InitializeAsync();
            
            CurrentView = CatalogViewModel;
            StatusText = "Ready";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusText = "Initialization failed";
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    partial void OnSelectedNavigationIndexChanged(int value)
    {
        CurrentView = value switch
        {
            0 => CatalogViewModel,
            1 => ChatViewModel,
            2 => DocumentViewModel,
            3 => SettingsViewModel,
            _ => CatalogViewModel
        };
    }
    
    [RelayCommand]
    private void NavigateToCatalog() => SelectedNavigationIndex = 0;
    
    [RelayCommand]
    private void NavigateToChat() => SelectedNavigationIndex = 1;
    
    [RelayCommand]
    private void NavigateToDocuments() => SelectedNavigationIndex = 2;
    
    [RelayCommand]
    private void NavigateToSettings() => SelectedNavigationIndex = 3;
}
