using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KaiROS.AI.Models;
using KaiROS.AI.Services;
using System.Collections.ObjectModel;

namespace KaiROS.AI.ViewModels;

public partial class DocumentViewModel : ViewModelBase
{
    private readonly IDocumentService _documentService;
    
    [ObservableProperty]
    private ObservableCollection<Document> _documents = new();
    
    [ObservableProperty]
    private bool _isLoading;
    
    [ObservableProperty]
    private string _statusMessage = "No documents loaded";
    
    public DocumentViewModel(IDocumentService documentService)
    {
        _documentService = documentService;
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
        
        StatusMessage = Documents.Count > 0 
            ? $"{Documents.Count} document(s) loaded" 
            : "No documents loaded";
    }
    
    [RelayCommand]
    private void ClearAll()
    {
        _documentService.ClearAllDocuments();
        Documents.Clear();
        StatusMessage = "No documents loaded";
    }
    
    public override Task InitializeAsync()
    {
        // Load any existing documents from service
        foreach (var doc in _documentService.LoadedDocuments)
        {
            if (!Documents.Any(d => d.Id == doc.Id))
            {
                Documents.Add(doc);
            }
        }
        
        StatusMessage = Documents.Count > 0 
            ? $"{Documents.Count} document(s) loaded" 
            : "No documents loaded. Upload documents to chat with them.";
        
        return Task.CompletedTask;
    }
}
