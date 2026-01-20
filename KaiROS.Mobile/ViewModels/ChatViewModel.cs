using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KaiROS.Mobile.Models;
using KaiROS.Mobile.Services;
using System.Collections.ObjectModel;

namespace KaiROS.Mobile.ViewModels;

public partial class ChatViewModel : ObservableObject
{
    private readonly MobileLLMService _llmService;
    private CancellationTokenSource? _generateCts;

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

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    public ChatViewModel(MobileLLMService llmService)
    {
        _llmService = llmService;

        _llmService.StatusChanged += (s, status) =>
            MainThread.BeginInvokeOnMainThread(() => StatusText = status);

        _llmService.StatsUpdated += (s, stats) =>
            MainThread.BeginInvokeOnMainThread(() => StatsText = $"{stats.FormattedSpeed} | {stats.GeneratedTokens} tokens");

        UpdateModelStatus();
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(UserInput) || !IsModelLoaded)
            return;

        var userMessage = UserInput.Trim();
        UserInput = string.Empty;

        // Add user message
        Messages.Add(new ChatMessage
        {
            Role = ChatRole.User,
            Content = userMessage
        });

        // Create assistant message for streaming
        var assistantMessage = new ChatMessage
        {
            Role = ChatRole.Assistant,
            Content = "",
            IsStreaming = true
        };
        Messages.Add(assistantMessage);

        try
        {
            IsGenerating = true;
            _generateCts = new CancellationTokenSource();

            var prompt = MobileLLMService.BuildPrompt(userMessage);
            var response = new System.Text.StringBuilder();

            await foreach (var token in _llmService.GenerateAsync(prompt, _generateCts.Token))
            {
                response.Append(token);

                // Update UI on main thread- ChatMessage is now observable so just update Content
                var currentContent = response.ToString();
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    assistantMessage.Content = currentContent;
                });
            }

            assistantMessage.IsStreaming = false;
        }
        catch (OperationCanceledException)
        {
            assistantMessage.Content += " [Cancelled]";
            assistantMessage.IsStreaming = false;
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
    private void ClearChat()
    {
        Messages.Clear();
        _llmService.ClearContext();
    }

    public void UpdateModelStatus()
    {
        IsModelLoaded = _llmService.IsModelLoaded;
        CurrentModelName = _llmService.CurrentModelName;
        StatusText = IsModelLoaded 
            ? $"Model: {CurrentModelName}" 
            : "No model loaded";
    }
}
