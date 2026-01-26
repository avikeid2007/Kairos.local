using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KaiROS.AI.Models;
using KaiROS.AI.Services;
using System.Collections.ObjectModel;
using System.Windows;

namespace KaiROS.AI.ViewModels;

public partial class DocumentViewModel : ViewModelBase
{
    private readonly IDocumentService _documentService;
    private readonly IRaasService _raasService;
    
    // --- Global Documents (Existing) ---
    [ObservableProperty]
    private ObservableCollection<Document> _documents = new();
    
    [ObservableProperty]
    private bool _isLoading;
    
    [ObservableProperty]
    private string _statusMessage = "No documents loaded";
    
    // --- RaaS Management (New) ---
    public ObservableCollection<RaasConfiguration> RaasConfigurations => _raasService.Configurations;

    [ObservableProperty]
    private string _newServiceName = "New Service";

    [ObservableProperty]
    private string _newServiceDescription = "";

    [ObservableProperty]
    private int _newServicePort = 5001;

    [ObservableProperty]
    private string _newServiceSystemPrompt = "You are a helpful AI assistant.";

    [ObservableProperty]
    private RaasConfiguration? _selectedConfiguration;
    
    [ObservableProperty]
    private bool _isCreatingService;
    
    partial void OnSelectedConfigurationChanged(RaasConfiguration? value)
    {
        if (value != null) IsCreatingService = false;
    }

    public DocumentViewModel(IDocumentService documentService, IRaasService raasService)
    {
        _documentService = documentService;
        _raasService = raasService;
    }
    
    // --- Global Document Commands ---

    [RelayCommand]
    private void StartCreatingService()
    {
        SelectedConfiguration = null;
        IsCreatingService = true;
    }

    [RelayCommand]
    private async Task LoadDocument()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "All Supported Documents|*.txt;*.md;*.docx;*.pdf;*.csv;*.json|PDF Documents (*.pdf)|*.pdf|Word Documents (*.docx)|*.docx|Text files (*.txt)|*.txt|Markdown (*.md)|*.md|All files (*.*)|*.*",
            Title = "Select a document to load"
        };
        
        if (dialog.ShowDialog() == true)
        {
            IsLoading = true;
            StatusMessage = "Loading document...";
            
            try
            {
                var doc = await _documentService.LoadDocumentAsync(dialog.FileName);
                Documents.Add(doc);
                StatusMessage = $"Loaded: {doc.FileName} ({doc.Chunks.Count} chunks)";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
    
    [RelayCommand]
    private void RemoveDocument(Document document)
    {
        if (document == null) return;
        _documentService.RemoveDocument(document.Id);
        Documents.Remove(document);
        StatusMessage = Documents.Count > 0 ? $"{Documents.Count} document(s) loaded" : "No documents loaded";
    }
    
    [RelayCommand]
    private void ClearAll()
    {
        _documentService.ClearAllDocuments();
        Documents.Clear();
        StatusMessage = "No documents loaded";
    }

    // --- RaaS Commands ---

    [RelayCommand]
    private async Task CreateService()
    {
        if (string.IsNullOrWhiteSpace(NewServiceName))
        {
            System.Windows.MessageBox.Show("Service Name is required.");
            return;
        }

        var config = new RaasConfiguration
        {
            Name = NewServiceName,
            Description = NewServiceDescription,
            Port = NewServicePort,
            SystemPrompt = NewServiceSystemPrompt
        };

        await _raasService.CreateConfigurationAsync(config);
        
        // Reset form
        NewServiceName = "New Service";
        NewServiceDescription = "";
        NewServicePort++;
        NewServiceSystemPrompt = "You are a helpful AI assistant.";
        
        IsCreatingService = false;
    }

    [RelayCommand]
    private async Task DeleteService(RaasConfiguration config)
    {
        if (System.Windows.MessageBox.Show($"Are you sure you want to delete '{config.Name}'?", "Confirm Delete", System.Windows.MessageBoxButton.YesNo) == System.Windows.MessageBoxResult.Yes)
        {
            await _raasService.DeleteConfigurationAsync(config.Id);
        }
    }

    [RelayCommand]
    private async Task StartService(RaasConfiguration config)
    {
        try
        {
            await _raasService.StartServiceAsync(config.Id);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to start service: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task StopService(RaasConfiguration config)
    {
        await _raasService.StopServiceAsync(config.Id);
    }
    
    [RelayCommand]
    private void OpenServiceUrl(RaasConfiguration config)
    {
        if (config == null || !config.IsRunning) return;
        
        try
        {
            var url = $"http://localhost:{config.Port}/";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to open browser: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task AddSourceToService(RaasConfiguration config)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
             Filter = "All Supported Files|*.txt;*.md;*.docx;*.pdf;*.csv;*.json",
             Title = $"Add file to {config.Name}"
        };

        if (dialog.ShowDialog() == true)
        {
            await _raasService.AddSourceAsync(config.Id, dialog.FileName);
        }
    }
    
    [RelayCommand]
    private async Task RemoveSourceFromService(RagSource source)
    {
        // We rely on SelectedConfiguration being the context.
        if (SelectedConfiguration != null && source != null)
        {
             await _raasService.RemoveSourceAsync(SelectedConfiguration.Id, source);
        }
    }

    public override async Task InitializeAsync()
    {
        // Load global documents
        foreach (var doc in _documentService.LoadedDocuments)
        {
            if (!Documents.Any(d => d.Id == doc.Id)) Documents.Add(doc);
        }
        
        // Initialize RaaS service
        await _raasService.InitializeAsync();
        
        StatusMessage = Documents.Count > 0 
            ? $"{Documents.Count} document(s) loaded" 
            : "No documents loaded. Upload documents to chat with them.";
    }
}
