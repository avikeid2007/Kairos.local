using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KaiROS.AI.Models;
using KaiROS.AI.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace KaiROS.AI.ViewModels;

public partial class ChatViewModel : ViewModelBase
{
    private readonly IChatService _chatService;
    private readonly IModelManagerService _modelManager;
    private readonly ISessionService _sessionService;
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
    private bool _hasActiveModel;
    
    [ObservableProperty]
    private string _activeModelInfo = "No model loaded";
    
    [ObservableProperty]
    private bool _isSessionListVisible = true;
    
    public ChatViewModel(IChatService chatService, IModelManagerService modelManager, ISessionService sessionService)
    {
        _chatService = chatService;
        _modelManager = modelManager;
        _sessionService = sessionService;
        
        _chatService.StatsUpdated += OnStatsUpdated;
        _modelManager.ModelLoaded += OnModelLoaded;
        _modelManager.ModelUnloaded += OnModelUnloaded;
    }
    
    public override async Task InitializeAsync()
    {
        await _sessionService.InitializeAsync();
        await LoadSessionsAsync();
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
        });
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
            await foreach (var token in _chatService.GenerateResponseStreamAsync(allMessages, _currentInferenceCts.Token))
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
    private async Task ExportChat()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json|Text files (*.txt)|*.txt",
            DefaultExt = "json",
            FileName = $"chat_export_{DateTime.Now:yyyyMMdd_HHmmss}"
        };
        
        if (dialog.ShowDialog() == true)
        {
            try
            {
                if (dialog.FileName.EndsWith(".json"))
                {
                    var export = Messages.Select(m => new
                    {
                        Role = m.Message.Role.ToString(),
                        m.Content,
                        m.Message.Timestamp
                    });
                    var json = JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(dialog.FileName, json);
                }
                else
                {
                    var lines = Messages.Select(m => $"[{m.Message.Role}] {m.Content}");
                    await File.WriteAllLinesAsync(dialog.FileName, lines);
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Export failed: {ex.Message}";
            }
        }
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
    
    public ChatMessageViewModel(ChatMessage message)
    {
        Message = message;
        _content = message.Content;
        _isStreaming = message.IsStreaming;
    }
    
    public void AppendContent(string text)
    {
        Content += text;
        Message.Content = Content;
    }
    
    public void CleanupContent()
    {
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
}
