using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using KaiROS.AI.Models;
using KaiROS.AI.Services;

using System.Collections.ObjectModel;
using System.IO;

namespace KaiROS.AI.ViewModels;

public partial class ChatViewModel : ViewModelBase
{
    private readonly IChatService _chatService;
    private readonly IModelManagerService _modelManager;
    private readonly ISessionService _sessionService;
    private readonly IExportService _exportService;
    private readonly IDocumentService _documentService;
    private readonly IRaasService _raasService;
    private CancellationTokenSource? _currentInferenceCts;

    [ObservableProperty]
    private ObservableCollection<ChatMessageViewModel> _messages = new();

    [ObservableProperty]
    private ObservableCollection<ChatSession> _sessions = new();

    [ObservableProperty]
    private ChatSession? _currentSession;

    [ObservableProperty]
    private string _userInput = string.Empty;

    [ObservableProperty]
    private bool _isWebSearchEnabled;

    [ObservableProperty]
    private string _systemPrompt = "You are a helpful, friendly AI assistant. Be concise and clear.";

    [ObservableProperty]
    private bool _isGenerating;

    [ObservableProperty]
    private bool _isSystemPromptExpanded;

    [ObservableProperty]
    private double _tokensPerSecond;

    [ObservableProperty]
    private int _totalTokens;

    [ObservableProperty]
    private string _memoryUsage = "N/A";

    [ObservableProperty]
    private string _elapsedTime = "0s";

    [ObservableProperty]
    private string _contextWindow = "N/A";

    [ObservableProperty]
    private string _gpuLayers = "N/A";

    [ObservableProperty]
    private bool _hasActiveModel;

    [ObservableProperty]
    private string _activeModelInfo = "No model loaded";

    [ObservableProperty]
    private bool _isSessionListVisible = true;

    [ObservableProperty]
    private bool _isSearchVisible;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isEnterToSendEnabled;

    [ObservableProperty]
    private string _currentDocumentName = string.Empty;
    
    // --- RAG Selection ---
    
    [ObservableProperty]
    private ObservableCollection<string> _availableKnowledgeBases = new() 
    { 
        "None" 
    };

    [ObservableProperty]
    private string _selectedKnowledgeBase = "None"; // Default to None

    [ObservableProperty]
    private int _globalRagDocumentCount;

    private string _currentDocumentContext = string.Empty;

    public ChatViewModel(IChatService chatService, IModelManagerService modelManager, ISessionService sessionService, IExportService exportService, IDocumentService documentService, IRaasService raasService)
    {
        _chatService = chatService;
        _modelManager = modelManager;
        _sessionService = sessionService;
        _exportService = exportService;
        _documentService = documentService;
        _raasService = raasService;

        IsWebSearchEnabled = false;
        IsEnterToSendEnabled = true;

        _chatService.StatsUpdated += OnStatsUpdated;
        _modelManager.ModelLoaded += OnModelLoaded;
        _modelManager.ModelUnloaded += OnModelUnloaded;
        
        // Listen to config changes to update list? 
        // Ideally we'd subscribe to _raasService.Configurations.CollectionChanged
        _raasService.Configurations.CollectionChanged += (s, e) => UpdateKnowledgeBaseList();
    }

    public override async Task InitializeAsync()
    {
        await _sessionService.InitializeAsync();
        await LoadSessionsAsync();
        GlobalRagDocumentCount = _documentService.LoadedDocuments.Count;
        
        await _raasService.InitializeAsync(); // ensure loaded
        UpdateKnowledgeBaseList();
    }
    
    private void UpdateKnowledgeBaseList()
    {
        // specific logic to preserve selection if possible
        var current = SelectedKnowledgeBase;
        
        AvailableKnowledgeBases.Clear();
        AvailableKnowledgeBases.Add("None");
        // User removed Global Knowledge tab, so we remove it here too
        
        foreach (var config in _raasService.Configurations)
        {
            // Only add running services? User said "saved RAG configuration", implies any? 
            // But if not running, we can't get context unless we load it on demand. 
            // For now, let's list all, but if not running, we might WARN or try to start it.
            // Requirement said "Use saved... as global RAG". Should probably work even if REST API is off?
            // If I implemented ApiServer to own the RagEngine, then I need the ApiServer to be Alive (Running) to use it.
            // So listing only Running services makes sense, OR start on demand.
            // Let's filter by Running for simplicity, or show all and check IsRunning.
            
            // Add all configurations
            // User can select them, and we handle the "Not Running" case in SendMessage
            AvailableKnowledgeBases.Add($"Service: {config.Name}");
        }
        
        if (AvailableKnowledgeBases.Contains(current))
        {
            SelectedKnowledgeBase = current;
        }
        else
        {
            SelectedKnowledgeBase = "None";
        }
    }

    private async Task LoadSessionsAsync()
    {
        var sessions = await _sessionService.GetAllSessionsAsync();
        Sessions.Clear();
        foreach (var session in sessions)
        {
            Sessions.Add(session);
        }
    }

    private void OnModelLoaded(object? sender, LLMModelInfo model)
    {
        HasActiveModel = true;
        ActiveModelInfo = $"{model.DisplayName} ({model.SizeText})";
    }

    private void OnModelUnloaded(object? sender, EventArgs e)
    {
        HasActiveModel = false;
        ActiveModelInfo = "No model loaded";
    }

    private void OnStatsUpdated(object? sender, InferenceStats stats)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            TokensPerSecond = Math.Round(stats.TokensPerSecond, 1);
            TotalTokens = stats.TotalTokens;
            MemoryUsage = stats.MemoryUsageText;
            ElapsedTime = $"{stats.ElapsedTime.TotalSeconds:F1}s";
            ContextWindow = stats.ContextSize > 0 ? $"{stats.ContextSize:N0}" : "N/A";
            GpuLayers = stats.GpuLayers >= 0 ? stats.GpuLayers.ToString() : "N/A";
        });
    }

    [RelayCommand]
    private async Task UploadDocument()
    {
        try
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "All Supported Files|*.txt;*.md;*.json;*.cs;*.xml;*.html;*.pdf;*.docx;*.doc|PDF Documents|*.pdf|Word Documents|*.docx;*.doc|Text Documents|*.txt;*.md;*.json;*.cs;*.xml;*.html|All Files|*.*",
                Title = "Select a document to chat with"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var filePath = openFileDialog.FileName;
                var fileName = Path.GetFileName(filePath);

                if (File.Exists(filePath))
                {
                    var extractedContent = await _documentService.GetDocumentContentAsync(filePath);

                    if (string.IsNullOrWhiteSpace(extractedContent))
                    {
                        System.Diagnostics.Debug.WriteLine($"[ChatViewModel] WARNING: No text extracted from {fileName}");
                        _currentDocumentContext = string.Empty;
                        CurrentDocumentName = string.Empty;
                    }
                    else
                    {
                        _currentDocumentContext = extractedContent;
                        CurrentDocumentName = fileName;
                        
                        if (_currentDocumentContext.Length > 50000)
                        {
                            _currentDocumentContext = _currentDocumentContext.Substring(0, 50000);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to upload: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RemoveDocument()
    {
        _currentDocumentContext = string.Empty;
        CurrentDocumentName = string.Empty;
    }

    [RelayCommand]
    private async Task SendMessage()
    {
        if (string.IsNullOrWhiteSpace(UserInput) || IsGenerating)
            return;

        if (!_chatService.IsModelLoaded)
        {
            Messages.Add(new ChatMessageViewModel(ChatMessage.Assistant("Please load a model first from the Models tab.")));
            return;
        }

        if (CurrentSession == null)
        {
            var modelName = _modelManager.ActiveModel?.DisplayName;
            CurrentSession = await _sessionService.CreateSessionAsync(modelName, SystemPrompt);
            Sessions.Insert(0, CurrentSession);
        }

        var userMessage = ChatMessage.User(UserInput);
        Messages.Add(new ChatMessageViewModel(userMessage));
        await _sessionService.AddMessageAsync(CurrentSession.Id, userMessage);
        CurrentSession.MessageCount++;

         if (CurrentSession.MessageCount == 1)
        {
            CurrentSession.Title = ChatSession.GenerateTitle(UserInput);
            await _sessionService.UpdateSessionAsync(CurrentSession);
        }

        // --- Determine RAG Context ---
        string? ragContext = null;
        if (SelectedKnowledgeBase == "Global Knowledge Base")
        {
            ragContext = _documentService.GetContextForQuery(UserInput, 3);
        }
        else if (SelectedKnowledgeBase.StartsWith("Service: "))
        {
             var serviceName = SelectedKnowledgeBase.Substring(9);
             var config = _raasService.Configurations.FirstOrDefault(c => c.Name == serviceName);
             if (config != null)
             {
                 var server = _raasService.GetServer(config.Id);
                 if (server != null && server.IsRunning)
                 {
                     ragContext = server.RagEngine.GetContext(UserInput, 3);
                 }
                 else
                 {
                     // Service not running
                     ragContext = "[System: The selected RAG service is not running. Answer based on general knowledge only.]";
                 }
             }
        }
        
        UserInput = string.Empty;

        var allMessages = new List<ChatMessage>();
        if (!string.IsNullOrWhiteSpace(SystemPrompt))
        {
            allMessages.Add(ChatMessage.System(SystemPrompt));
        }
        allMessages.AddRange(Messages.Select(m => m.Message));

        var assistantMessage = ChatMessage.Assistant(string.Empty);
        assistantMessage.IsStreaming = true;
        var assistantVm = new ChatMessageViewModel(assistantMessage);
        Messages.Add(assistantVm);

        IsGenerating = true;
        _currentInferenceCts = new CancellationTokenSource();

        try
        {
            await foreach (var token in _chatService.GenerateResponseStreamAsync(
                allMessages,
                IsWebSearchEnabled,
                _currentDocumentContext, 
                ragContext,      // Pass resolved context!
                _currentInferenceCts.Token))
            {
                assistantVm.AppendContent(token);
            }
        }
        catch (OperationCanceledException)
        {
            assistantVm.AppendContent("\n[Generation stopped]");
        }
        catch (Exception ex)
        {
            assistantVm.Content = $"Error: {ex.Message}";
        }
        finally
        {
            assistantVm.CleanupContent();
            assistantVm.Message.IsStreaming = false;
            assistantVm.IsStreaming = false;
            IsGenerating = false;
            _currentInferenceCts = null;

            if (CurrentSession != null && !string.IsNullOrEmpty(assistantVm.Content))
            {
                await _sessionService.AddMessageAsync(CurrentSession.Id, assistantVm.Message);
                CurrentSession.MessageCount++;
            }
        }
    }

    [RelayCommand]
    private void StopGeneration()
    {
        _currentInferenceCts?.Cancel();
    }

    [RelayCommand]
    private void ClearChat()
    {
        Messages.Clear();
        _chatService.ClearContext();
        CurrentSession = null;
        TokensPerSecond = 0;
        TotalTokens = 0;
        MemoryUsage = "N/A";
        ElapsedTime = "0s";
        RemoveDocument();
    }

    [RelayCommand]
    private async Task NewSession()
    {
        CurrentSession = null;
        Messages.Clear();
        _chatService.ClearContext();
        TokensPerSecond = 0;
        TotalTokens = 0;
        MemoryUsage = "N/A";
        ElapsedTime = "0s";
        RemoveDocument();
    }

    [RelayCommand]
    private async Task LoadSession(ChatSession session)
    {
        if (session == null) return;
        CurrentSession = await _sessionService.GetSessionAsync(session.Id);
        if (CurrentSession == null) return;

        Messages.Clear();
        _chatService.ClearContext();

        foreach (var msg in CurrentSession.Messages)
        {
            Messages.Add(new ChatMessageViewModel(msg));
        }

        if (!string.IsNullOrEmpty(CurrentSession.SystemPrompt))
        {
            SystemPrompt = CurrentSession.SystemPrompt;
        }

        TokensPerSecond = 0;
        TotalTokens = 0;
        MemoryUsage = "N/A";
        ElapsedTime = "0s";
    }

    [RelayCommand]
    private async Task DeleteSession(ChatSession session)
    {
        if (session == null) return;
        await _sessionService.DeleteSessionAsync(session.Id);
        Sessions.Remove(session);
        if (CurrentSession?.Id == session.Id)
        {
            CurrentSession = null;
            Messages.Clear();
            _chatService.ClearContext();
        }
    }

    [RelayCommand]
    private void ToggleSessionList()
    {
        IsSessionListVisible = !IsSessionListVisible;
    }

    [RelayCommand]
    private void ToggleSearch()
    {
        IsSearchVisible = !IsSearchVisible;
        if (!IsSearchVisible) SearchText = string.Empty;
    }

    [RelayCommand]
    private void CloseSearch()
    {
        IsSearchVisible = false;
        SearchText = string.Empty;
    }

    [RelayCommand]
    private async Task ExportChatAsMarkdown()
    {
        if (CurrentSession == null || Messages.Count == 0) return;
        var messages = Messages.Select(m => m.Message).ToList();
        await _exportService.ExportWithDialogAsync(CurrentSession, messages, ExportFormat.Markdown);
    }

    [RelayCommand]
    private async Task ExportChatAsJson()
    {
        if (CurrentSession == null || Messages.Count == 0) return;
        var messages = Messages.Select(m => m.Message).ToList();
        await _exportService.ExportWithDialogAsync(CurrentSession, messages, ExportFormat.Json);
    }

    [RelayCommand]
    private async Task ExportChatAsText()
    {
        if (CurrentSession == null || Messages.Count == 0) return;
        var messages = Messages.Select(m => m.Message).ToList();
        await _exportService.ExportWithDialogAsync(CurrentSession, messages, ExportFormat.Text);
    }

    [RelayCommand]
    private void ToggleSystemPrompt()
    {
        IsSystemPromptExpanded = !IsSystemPromptExpanded;
    }
    
    [RelayCommand]
    private void CopyContent() { /* ... handled in item view model or pass parameter ... */ }
}

public partial class ChatMessageViewModel : ObservableObject
{
    public ChatMessage Message { get; }

    [ObservableProperty]
    private string _content;

    [ObservableProperty]
    private bool _isStreaming;

    public bool IsUser => Message.Role == ChatRole.User;
    public bool IsAssistant => Message.Role == ChatRole.Assistant;
    public bool IsSystem => Message.Role == ChatRole.System;
    public string Timestamp => Message.Timestamp.ToString("HH:mm");

    private readonly System.Text.StringBuilder _tokenBuffer = new();
    private System.Windows.Threading.DispatcherTimer? _flushTimer;
    private int _pendingTokenCount;
    private const int BATCH_TOKEN_COUNT = 15;  
    private const int FLUSH_INTERVAL_MS = 50;  

    public ChatMessageViewModel(ChatMessage message)
    {
        Message = message;
        _content = message.Content;
        _isStreaming = message.IsStreaming;
    }

    public void AppendContent(string text)
    {
        _tokenBuffer.Append(text);
        _pendingTokenCount++;
        if (_pendingTokenCount >= BATCH_TOKEN_COUNT) FlushBuffer();
        else EnsureFlushTimer();
    }

    private void EnsureFlushTimer()
    {
        if (_flushTimer == null)
        {
            _flushTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(FLUSH_INTERVAL_MS)
            };
            _flushTimer.Tick += (s, e) => FlushBuffer();
        }
        if (!_flushTimer.IsEnabled) _flushTimer.Start();
    }

    private void FlushBuffer()
    {
        _flushTimer?.Stop();
        if (_tokenBuffer.Length == 0) return;
        Content += _tokenBuffer.ToString();
        Message.Content = Content;
        _tokenBuffer.Clear();
        _pendingTokenCount = 0;
    }

    public void FinalizeStreaming()
    {
        FlushBuffer();
        _flushTimer?.Stop();
        _flushTimer = null;
    }

    public void CleanupContent()
    {
        FlushBuffer();
        var unwantedPatterns = new[] { "###", "\n###", "User:", "\nUser:", "Human:", "\nHuman:", "<|im_end|>", "<|assistant|>" };
        var cleaned = Content;
        foreach (var pattern in unwantedPatterns) cleaned = cleaned.Replace(pattern, "");
        Content = cleaned.Trim();
        Message.Content = Content;
    }

    [RelayCommand]
    private void CopyContent()
    {
        if (!string.IsNullOrEmpty(Content))
        {
            try { System.Windows.Clipboard.SetText(Content); } catch { }
        }
    }
}
