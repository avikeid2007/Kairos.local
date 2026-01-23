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
    private bool _isGlobalRagEnabled;

    [ObservableProperty]
    private bool _isEnterToSendEnabled;

    [ObservableProperty]
    private string _currentDocumentName = string.Empty;

    [ObservableProperty]
    private int _globalRagDocumentCount;

    private string _currentDocumentContext = string.Empty;

    private readonly IDocumentService _documentService;

    public ChatViewModel(IChatService chatService, IModelManagerService modelManager, ISessionService sessionService, IExportService exportService, IDocumentService documentService)
    {
        _chatService = chatService;
        _modelManager = modelManager;
        _sessionService = sessionService;
        _exportService = exportService;
        _documentService = documentService;

        IsWebSearchEnabled = false; // Default to off
        IsGlobalRagEnabled = false; // Default to off
        IsEnterToSendEnabled = true; // Default to On (Ctrl+Enter to send)

        _chatService.StatsUpdated += OnStatsUpdated;
        _modelManager.ModelLoaded += OnModelLoaded;
        _modelManager.ModelUnloaded += OnModelUnloaded;
    }

    public override async Task InitializeAsync()
    {
        await _sessionService.InitializeAsync();
        await LoadSessionsAsync();
        GlobalRagDocumentCount = _documentService.LoadedDocuments.Count;
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
                    // Use DocumentService to parse content (supports PDF, Docx, etc.)
                    var extractedContent = await _documentService.GetDocumentContentAsync(filePath);

                    if (string.IsNullOrWhiteSpace(extractedContent))
                    {
                        System.Diagnostics.Debug.WriteLine($"[ChatViewModel] WARNING: No text extracted from {fileName}");
                        // You might want to bind this to a UI property to show the user
                        _currentDocumentContext = string.Empty;
                        CurrentDocumentName = string.Empty;
                        // Assuming there is a way to show error, possibly reuse existing mechanism or just log for now
                        // Ideally: ErrorMessage = "No text found in document. It might be scanned/image-based.";
                    }
                    else
                    {
                        _currentDocumentContext = extractedContent;
                        CurrentDocumentName = fileName;
                        System.Diagnostics.Debug.WriteLine($"[ChatViewModel] Extracted {_currentDocumentContext.Length} chars from {fileName}");

                        // Optional: trim if too large
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

        // Create or get current session
        if (CurrentSession == null)
        {
            var modelName = _modelManager.ActiveModel?.DisplayName;
            CurrentSession = await _sessionService.CreateSessionAsync(modelName, SystemPrompt);
            Sessions.Insert(0, CurrentSession);
        }

        // Add user message
        var userMessage = ChatMessage.User(UserInput);
        Messages.Add(new ChatMessageViewModel(userMessage));
        await _sessionService.AddMessageAsync(CurrentSession.Id, userMessage);
        CurrentSession.MessageCount++;

        // Update session title from first message (when it's the first user message)
        if (CurrentSession.MessageCount == 1)
        {
            CurrentSession.Title = ChatSession.GenerateTitle(UserInput);
            await _sessionService.UpdateSessionAsync(CurrentSession);
        }

        UserInput = string.Empty;

        // Prepare messages for inference
        var allMessages = new List<ChatMessage>();
        if (!string.IsNullOrWhiteSpace(SystemPrompt))
        {
            allMessages.Add(ChatMessage.System(SystemPrompt));
        }
        allMessages.AddRange(Messages.Select(m => m.Message));

        // Create assistant message for streaming
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
                _currentDocumentContext, // Pass uploaded doc
                IsGlobalRagEnabled,      // Pass global RAG toggle
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
            // Clean up the final message content (remove ### and other artifacts)
            assistantVm.CleanupContent();
            assistantVm.Message.IsStreaming = false;
            assistantVm.IsStreaming = false;
            IsGenerating = false;
            _currentInferenceCts = null;

            // Save assistant message to database
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
        RemoveDocument(); // Clear uploaded doc on reset
    }

    [RelayCommand]
    private async Task NewSession()
    {
        // Save current session if exists
        CurrentSession = null;
        Messages.Clear();
        _chatService.ClearContext();
        TokensPerSecond = 0;
        TotalTokens = 0;
        MemoryUsage = "N/A";
        ElapsedTime = "0s";
        RemoveDocument(); // Clear uploaded doc on new session
    }

    [RelayCommand]
    private async Task LoadSession(ChatSession session)
    {
        if (session == null) return;

        // Load session and its messages
        CurrentSession = await _sessionService.GetSessionAsync(session.Id);
        if (CurrentSession == null) return;

        // Clear current messages and load from session
        Messages.Clear();
        _chatService.ClearContext();

        foreach (var msg in CurrentSession.Messages)
        {
            Messages.Add(new ChatMessageViewModel(msg));
        }

        // Restore system prompt if saved
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

        // If deleting current session, clear it
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
        if (!IsSearchVisible)
        {
            SearchText = string.Empty;
        }
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
        if (CurrentSession == null || Messages.Count == 0)
        {
            ErrorMessage = "No conversation to export";
            return;
        }

        var messages = Messages.Select(m => m.Message).ToList();
        await _exportService.ExportWithDialogAsync(CurrentSession, messages, ExportFormat.Markdown);
    }

    [RelayCommand]
    private async Task ExportChatAsJson()
    {
        if (CurrentSession == null || Messages.Count == 0)
        {
            ErrorMessage = "No conversation to export";
            return;
        }

        var messages = Messages.Select(m => m.Message).ToList();
        await _exportService.ExportWithDialogAsync(CurrentSession, messages, ExportFormat.Json);
    }

    [RelayCommand]
    private async Task ExportChatAsText()
    {
        if (CurrentSession == null || Messages.Count == 0)
        {
            ErrorMessage = "No conversation to export";
            return;
        }

        var messages = Messages.Select(m => m.Message).ToList();
        await _exportService.ExportWithDialogAsync(CurrentSession, messages, ExportFormat.Text);
    }

    [RelayCommand]
    private void ToggleSystemPrompt()
    {
        IsSystemPromptExpanded = !IsSystemPromptExpanded;
    }
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

    // Streaming optimization - batch tokens for smoother UI updates
    private readonly System.Text.StringBuilder _tokenBuffer = new();
    private System.Windows.Threading.DispatcherTimer? _flushTimer;
    private int _pendingTokenCount;
    private const int BATCH_TOKEN_COUNT = 15;  // Flush after this many tokens
    private const int FLUSH_INTERVAL_MS = 50;   // Or flush after this many ms

    public ChatMessageViewModel(ChatMessage message)
    {
        Message = message;
        _content = message.Content;
        _isStreaming = message.IsStreaming;
    }

    public void AppendContent(string text)
    {
        // Buffer the token instead of immediate UI update
        _tokenBuffer.Append(text);
        _pendingTokenCount++;

        // Flush if buffer is large enough
        if (_pendingTokenCount >= BATCH_TOKEN_COUNT)
        {
            FlushBuffer();
        }
        else
        {
            // Start timer to flush after interval
            EnsureFlushTimer();
        }
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

        if (!_flushTimer.IsEnabled)
        {
            _flushTimer.Start();
        }
    }

    private void FlushBuffer()
    {
        _flushTimer?.Stop();

        if (_tokenBuffer.Length == 0) return;

        // Batch update the Content property (single UI update)
        Content += _tokenBuffer.ToString();
        Message.Content = Content;

        _tokenBuffer.Clear();
        _pendingTokenCount = 0;
    }

    public void FinalizeStreaming()
    {
        // Called when streaming ends - flush any remaining tokens
        FlushBuffer();
        _flushTimer?.Stop();
        _flushTimer = null;
    }

    public void CleanupContent()
    {
        // Ensure all buffered content is flushed first
        FlushBuffer();

        // Remove unwanted tokens from the final content
        var unwantedPatterns = new[] { "###", "\n###", "User:", "\nUser:", "Human:", "\nHuman:", "<|im_end|>", "<|assistant|>" };
        var cleaned = Content;
        foreach (var pattern in unwantedPatterns)
        {
            cleaned = cleaned.Replace(pattern, "");
        }
        // Trim whitespace
        cleaned = cleaned.Trim();
        Content = cleaned;
        Message.Content = Content;
    }

    [RelayCommand]
    private void CopyContent()
    {
        if (!string.IsNullOrEmpty(Content))
        {
            try
            {
                System.Windows.Clipboard.SetText(Content);
            }
            catch (Exception)
            {
                // Clipboard access can fail if another app is using it
            }
        }
    }
}

