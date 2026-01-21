using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KaiROS.Mobile.Models;
using KaiROS.Mobile.Services;
using System.Collections.ObjectModel;

namespace KaiROS.Mobile.ViewModels;

public partial class ChatHistoryViewModel : ObservableObject
{
    private readonly ChatDatabaseService _databaseService;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    public ObservableCollection<ChatSession> Sessions { get; } = new();

    public ChatHistoryViewModel(ChatDatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    [RelayCommand]
    private async Task LoadSessionsAsync()
    {
        try
        {
            IsLoading = true;
            var sessions = await _databaseService.GetAllSessionsAsync();
            
            Sessions.Clear();
            foreach (var session in sessions)
            {
                // Filter by search query if present
                if (string.IsNullOrWhiteSpace(SearchQuery) || 
                    session.Title.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
                {
                    Sessions.Add(session);
                }
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DeleteSessionAsync(ChatSession session)
    {
        var confirm = await Shell.Current.DisplayAlert(
            "Delete Chat", 
            $"Delete \"{session.Title}\"?", 
            "Delete", 
            "Cancel");
        
        if (!confirm) return;

        await _databaseService.DeleteSessionAsync(session.Id);
        Sessions.Remove(session);
    }

    [RelayCommand]
    private async Task OpenSessionAsync(ChatSession session)
    {
        // Navigate to chat page with session ID
        await Shell.Current.GoToAsync($"//Chat?sessionId={session.Id}");
    }

    partial void OnSearchQueryChanged(string value)
    {
        // Reload sessions when search query changes
        _ = LoadSessionsAsync();
    }
}
