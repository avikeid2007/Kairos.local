using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KaiROS.Mobile.Models;
using KaiROS.Mobile.Services;
using System.Collections.ObjectModel;

namespace KaiROS.Mobile.ViewModels;

[QueryProperty(nameof(SessionId), "sessionId")]
public partial class ChatViewModel : ObservableObject
{
    private readonly MobileLLMService _llmService;
    private readonly ChatDatabaseService _databaseService;
    private readonly PromptLibraryService _promptService;
    private readonly VoiceService _voiceService;
    private readonly DocumentService _documentService;
    private CancellationTokenSource? _generateCts;
    private CancellationTokenSource? _speakCts;
    private int _currentSessionId;

    [ObservableProperty]
    private string _userInput = string.Empty;

    [ObservableProperty]
    private string _statusText = "No model loaded";

    [ObservableProperty]
    private bool _isGenerating;

    [ObservableProperty]
    private bool _isModelLoaded;

    [ObservableProperty]
    private string? _currentModelName;

    [ObservableProperty]
    private string _statsText = string.Empty;

    [ObservableProperty]
    private bool _isListening;

    [ObservableProperty]
    private bool _isSpeaking;

    [ObservableProperty]
    private bool _hasDocuments;

    [ObservableProperty]
    private string _documentStatusText = string.Empty;

    public string? SessionId { get; set; }

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    public ChatViewModel(MobileLLMService llmService, ChatDatabaseService databaseService, PromptLibraryService promptService, VoiceService voiceService, DocumentService documentService)
    {
        _llmService = llmService;
        _databaseService = databaseService;
        _promptService = promptService;
        _voiceService = voiceService;
        _documentService = documentService;

        _llmService.StatusChanged += (s, status) =>
            MainThread.BeginInvokeOnMainThread(() => StatusText = status);

        _llmService.StatsUpdated += (s, stats) =>
            MainThread.BeginInvokeOnMainThread(() => StatsText = $"{stats.FormattedSpeed} | {stats.GeneratedTokens} tokens");

        _voiceService.ListeningStarted += (s, e) =>
            MainThread.BeginInvokeOnMainThread(() => IsListening = true);

        _voiceService.ListeningStopped += (s, e) =>
            MainThread.BeginInvokeOnMainThread(() => IsListening = false);

        _documentService.DocumentsChanged += (s, e) =>
            MainThread.BeginInvokeOnMainThread(() => UpdateDocumentStatus());

        UpdateModelStatus();
        UpdateDocumentStatus();
    }

    /// <summary>
    /// Initialize or load session when page appears.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (!string.IsNullOrEmpty(SessionId) && int.TryParse(SessionId, out var sessionId))
        {
            // Load existing session
            await LoadSessionAsync(sessionId);
        }
        else if (_currentSessionId == 0)
        {
            // Create new session
            await CreateNewSessionAsync();
        }
    }

    private async Task CreateNewSessionAsync()
    {
        var session = await _databaseService.CreateSessionAsync(CurrentModelName);
        _currentSessionId = session.Id;
        Messages.Clear();
    }

    private async Task LoadSessionAsync(int sessionId)
    {
        _currentSessionId = sessionId;
        var messages = await _databaseService.GetMessagesAsync(sessionId);

        Messages.Clear();
        foreach (var msg in messages)
        {
            Messages.Add(msg);
        }

        SessionId = null; // Clear to allow new session on next clear
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(UserInput) || !IsModelLoaded)
            return;

        var userMessage = UserInput.Trim();
        UserInput = string.Empty;

        // Add user message
        var userMsg = new ChatMessage
        {
            Role = ChatRole.User,
            Content = userMessage,
            SessionId = _currentSessionId
        };
        Messages.Add(userMsg);
        await _databaseService.SaveMessageAsync(userMsg);

        // Update session title from first message
        await _databaseService.UpdateSessionTitleFromFirstMessageAsync(_currentSessionId);

        // Create assistant message for streaming
        var assistantMessage = new ChatMessage
        {
            Role = ChatRole.Assistant,
            Content = "",
            SessionId = _currentSessionId,
            IsStreaming = true
        };
        Messages.Add(assistantMessage);

        try
        {
            IsGenerating = true;
            _generateCts = new CancellationTokenSource();

            var prompt = MobileLLMService.BuildPrompt(userMessage, _promptService.GetActivePromptText(), _documentService.BuildContextString());
            var response = new System.Text.StringBuilder();

            await foreach (var token in _llmService.GenerateAsync(prompt, _generateCts.Token))
            {
                response.Append(token);

                var currentContent = response.ToString();
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    assistantMessage.Content = currentContent;
                });
            }

            assistantMessage.IsStreaming = false;

            // Save assistant message after generation completes
            await _databaseService.SaveMessageAsync(assistantMessage);
        }
        catch (OperationCanceledException)
        {
            assistantMessage.Content += " [Cancelled]";
            assistantMessage.IsStreaming = false;
            await _databaseService.SaveMessageAsync(assistantMessage);
        }
        finally
        {
            IsGenerating = false;
            _generateCts?.Dispose();
            _generateCts = null;
        }
    }

    [RelayCommand]
    private void StopGeneration()
    {
        _generateCts?.Cancel();
    }

    [RelayCommand]
    private async Task ClearChatAsync()
    {
        Messages.Clear();
        _llmService.ClearContext();
        _currentSessionId = 0;
        await CreateNewSessionAsync();
    }

    [RelayCommand]
    private async Task NewChatAsync()
    {
        await ClearChatAsync();
    }

    public void UpdateModelStatus()
    {
        IsModelLoaded = _llmService.IsModelLoaded;
        CurrentModelName = _llmService.CurrentModelName;
        StatusText = IsModelLoaded
            ? $"Model: {CurrentModelName}"
            : "No model loaded";
    }

    [RelayCommand]
    private async Task StartVoiceInputAsync()
    {
        if (IsListening)
        {
            _voiceService.StopListening();
            return;
        }

        var result = await _voiceService.ListenAsync();
        if (!string.IsNullOrWhiteSpace(result))
        {
            UserInput = result;
            // Auto-send if model is loaded
            if (IsModelLoaded)
            {
                await SendMessageAsync();
            }
        }
    }

    [RelayCommand]
    private async Task SpeakResponseAsync(ChatMessage message)
    {
        if (message.Role != ChatRole.Assistant || string.IsNullOrWhiteSpace(message.Content))
            return;

        if (IsSpeaking)
        {
            _speakCts?.Cancel();
            IsSpeaking = false;
            return;
        }

        try
        {
            IsSpeaking = true;
            _speakCts = new CancellationTokenSource();
            await _voiceService.SpeakAsync(message.Content, _speakCts.Token);
        }
        finally
        {
            IsSpeaking = false;
            _speakCts?.Dispose();
            _speakCts = null;
        }
    }

    [RelayCommand]
    private async Task CopyMessage(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return;

        await Clipboard.Default.SetTextAsync(content);
        await Toast.Make("Message copied to clipboard").Show();
    }

    [RelayCommand]
    private async Task AddDocumentAsync()
    {
        var doc = await _documentService.PickAndLoadDocumentAsync();
        if (doc != null)
        {
            await Shell.Current.DisplayAlert("Document Added",
                $"Loaded: {doc.FileName}\n({doc.FormattedSize})", "OK");
        }
    }

    [RelayCommand]
    private void ClearDocuments()
    {
        _documentService.ClearAllDocuments();
    }

    private void UpdateDocumentStatus()
    {
        HasDocuments = _documentService.HasDocuments;
        var count = _documentService.LoadedDocuments.Count;
        DocumentStatusText = count > 0
            ? $"📎 {count} doc{(count > 1 ? "s" : "")}"
            : "";
    }
}
